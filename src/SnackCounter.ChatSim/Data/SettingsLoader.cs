using System.Text.Json;

using SnackCounter;

namespace SnackCounter.ChatSim;

// Reads config/settings.json: timers, budgets per office, and report exchange rates.
// If anything is missing or wrong, it stops with a clear message instead of guessing a value.
public class SettingsLoader
{
    public static Settings LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException("Settings file not found: " + path);
        }
        return LoadFromJson(File.ReadAllText(path));
    }

    public static Settings LoadFromJson(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // "timers": { ... }
        JsonElement timers = Required(root, "timers");
        SnackConfig config = new SnackConfig(
            TimeSpan.FromMinutes((double)PositiveNumber(timers, "acceptWindowMinutes")),
            TimeSpan.FromHours((double)PositiveNumber(timers, "completeWindowHours")),
            TimeSpan.FromDays((double)PositiveNumber(timers, "fairnessWindowDays")));

        // "officeHours": [ { "office": "Oslo", "timeZone": "Europe/Oslo", "openHour": 10, "closeHour": 17,
        //                   "workDays": ["Mon", "Tue", "Wed", "Thu", "Fri"] }, ... ]
        // Optional: an office without an entry is always open.
        if (root.TryGetProperty("officeHours", out JsonElement hoursList))
        {
            foreach (JsonElement h in hoursList.EnumerateArray())
            {
                config.Hours.Add(ReadHours(h));
            }
        }

        // "budgets": [ { "office": "Oslo", ... }, ... ]
        List<OfficeBudget> budgets = new List<OfficeBudget>();
        foreach (JsonElement b in Required(root, "budgets").EnumerateArray())
        {
            string officeText = Text(b, "office");
            if (!Enum.TryParse(officeText, true, out Office office))
            {
                throw new InvalidDataException("Unknown office in settings: " + officeText);
            }
            decimal monthly = PositiveNumber(b, "monthlyBudget");
            decimal min = PositiveNumber(b, "typicalPurchaseMin");
            decimal max = PositiveNumber(b, "typicalPurchaseMax");
            if (min > max || max > monthly)
            {
                throw new InvalidDataException("Budget for " + office + ": need typicalPurchaseMin <= typicalPurchaseMax <= monthlyBudget");
            }
            budgets.Add(new OfficeBudget(office, Text(b, "currency"), monthly, min, max));
        }

        // Every office must have a budget.
        foreach (Office office in Enum.GetValues<Office>())
        {
            bool found = false;
            foreach (OfficeBudget b in budgets)
            {
                if (b.Office == office) found = true;
            }
            if (!found)
            {
                throw new InvalidDataException("No budget in settings for " + office);
            }
        }

        // "report": { "combinedCurrency": "NOK", "rates": { "NOK": 1, "INR": 0.13 } }
        JsonElement report = Required(root, "report");
        string reportCurrency = Text(report, "combinedCurrency");
        Dictionary<string, decimal> rates = new Dictionary<string, decimal>();
        foreach (JsonProperty rate in Required(report, "rates").EnumerateObject())
        {
            rates[rate.Name] = rate.Value.GetDecimal();
        }

        return new Settings(config, budgets, reportCurrency, rates);
    }

    // Writes the current settings back, so an admin's change from chat survives a restart.
    public static void SaveToFile(string path, Settings settings)
    {
        using FileStream file = File.Create(path);
        using Utf8JsonWriter w = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true });
        w.WriteStartObject();

        w.WriteStartObject("timers");
        w.WriteNumber("acceptWindowMinutes", settings.Timers.AcceptWindow.TotalMinutes);
        w.WriteNumber("completeWindowHours", settings.Timers.CompleteWindow.TotalHours);
        w.WriteNumber("fairnessWindowDays", settings.Timers.FairnessWindow.TotalDays);
        w.WriteEndObject();

        w.WriteStartArray("officeHours");
        foreach (OfficeHours h in settings.Timers.Hours)
        {
            w.WriteStartObject();
            w.WriteString("office", h.Office.ToString());
            w.WriteString("timeZone", h.TimeZone.Id);
            w.WriteNumber("openHour", h.OpenHour);
            w.WriteNumber("closeHour", h.CloseHour);
            w.WriteStartArray("workDays");
            foreach (DayOfWeek d in h.WorkDays) w.WriteStringValue(d.ToString().Substring(0, 3));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("budgets");
        foreach (OfficeBudget b in settings.Budgets)
        {
            w.WriteStartObject();
            w.WriteString("office", b.Office.ToString());
            w.WriteString("currency", b.Currency);
            w.WriteNumber("monthlyBudget", b.MonthlyBudgetMinor / 100m);
            w.WriteNumber("typicalPurchaseMin", b.TypicalMinMinor / 100m);
            w.WriteNumber("typicalPurchaseMax", b.TypicalMaxMinor / 100m);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartObject("report");
        w.WriteString("combinedCurrency", settings.ReportCurrency);
        w.WriteStartObject("rates");
        foreach (KeyValuePair<string, decimal> rate in settings.ReportRates) w.WriteNumber(rate.Key, rate.Value);
        w.WriteEndObject();
        w.WriteEndObject();

        w.WriteEndObject();
    }

    private static OfficeHours ReadHours(JsonElement h)
    {
        string officeText = Text(h, "office");
        if (!Enum.TryParse(officeText, true, out Office office))
        {
            throw new InvalidDataException("Unknown office in officeHours: " + officeText);
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(Text(h, "timeZone"));
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidDataException("Unknown timeZone for " + office + ": " + Text(h, "timeZone"));
        }

        int open = Required(h, "openHour").GetInt32();
        int close = Required(h, "closeHour").GetInt32();
        if (open < 0 || close > 24 || open >= close)
        {
            throw new InvalidDataException("Office hours for " + office + ": need 0 <= openHour < closeHour <= 24");
        }

        // "Mon".."Sun" (or full names), at least one.
        List<DayOfWeek> days = new List<DayOfWeek>();
        foreach (JsonElement d in Required(h, "workDays").EnumerateArray())
        {
            string dayText = d.GetString() ?? "";
            bool found = false;
            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            {
                if (dayText.Length >= 3 && day.ToString().StartsWith(dayText, StringComparison.OrdinalIgnoreCase))
                {
                    days.Add(day);
                    found = true;
                }
            }
            if (!found) throw new InvalidDataException("Unknown work day for " + office + ": " + dayText);
        }
        if (days.Count == 0) throw new InvalidDataException("Office hours for " + office + ": need at least one work day");

        return new OfficeHours(office, zone, open, close, days);
    }

    private static JsonElement Required(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            throw new InvalidDataException("Missing '" + name + "' in settings");
        }
        return value;
    }

    private static string Text(JsonElement parent, string name)
    {
        string? text = Required(parent, name).GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("'" + name + "' is empty in settings");
        }
        return text;
    }

    private static decimal PositiveNumber(JsonElement parent, string name)
    {
        decimal value = Required(parent, name).GetDecimal();
        if (value <= 0)
        {
            throw new InvalidDataException("'" + name + "' must be more than 0 in settings");
        }
        return value;
    }
}
