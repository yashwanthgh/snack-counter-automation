using System.Globalization;
using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.ChatSim;

// The "chat adapter": turns typed chat lines into calls on the core.
// A real Slack/Teams bot would do the same job for webhook events and button clicks.
public class CommandHandler
{
    private List<Employee> _people;
    private RefillService _refills;
    private BudgetService _budget;
    private ReportService _report;
    private FakeHr _hr;
    private SimClock _clock;
    private Settings _settings;
    private string _settingsPath;

    public CommandHandler(List<Employee> people, RefillService refills, BudgetService budget, ReportService report, FakeHr hr, SimClock clock, Settings settings, string settingsPath)
    {
        _budget = budget;
        _settingsPath = settingsPath;
        _people = people;
        _refills = refills;
        _report = report;
        _hr = hr;
        _clock = clock;
        _settings = settings;
    }

    // Returns false when the user wants to quit.
    public bool Handle(string line)
    {
        string[] w = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) return true;

        switch (w[0].ToLowerInvariant())
        {
            case "quit": return false;
            case "help": Say(Help); return true;
            case "time": AdvanceTime(w); return true;
            case "hr": HandleHr(w); return true;
            case "status": PrintStatus(); return true;
            case "report":
                DateTimeOffset now = _clock.GetNow();
                Console.WriteLine(_report.Build(now.Year, now.Month, _settings.ReportCurrency, _settings.ReportRates).ToText());
                return true;
        }

        // "<name>: <message>"
        int colon = line.IndexOf(':');
        Employee? person = colon > 0 ? FindPerson(line.Substring(0, colon).Trim().ToLowerInvariant()) : null;
        if (person == null)
        {
            Say("Not understood. Type 'help'.");
            return true;
        }
        HandlePersonMessage(person, line.Substring(colon + 1).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return true;
    }

    private void HandlePersonMessage(Employee person, string[] w)
    {
        if (w.Length == 0) return;
        DateTimeOffset now = _clock.GetNow();
        string command = w[0].ToLowerInvariant();

        if (command == "empty")
        {
            Refill r = _refills.ReportEmpty(person.CurrentOffice(now), person.EmployeeId);
            Say(person.Name + " reported the " + r.Office + " box empty (" + r.ShortId() + ", " + r.State + ").");
            return;
        }
        if (command == "away")
        {
            person.UnavailableUntil = now.AddDays(ParseInt(w, 1, 7));
            Say(person.Name + " is away until " + person.UnavailableUntil.Value.ToString("dd MMM") + ".");
            return;
        }
        if (command == "set")
        {
            HandleAdmin(person, w);
            return;
        }
        if (command == "visiting" && w.Length >= 2 && Enum.TryParse(w[1], true, out Office office))
        {
            person.VisitingOffice = office;
            person.VisitingUntil = now.AddDays(ParseInt(w, 2, 7));
            Say(person.Name + " is in " + office + " until " + person.VisitingUntil.Value.ToString("dd MMM") + ".");
            return;
        }

        Refill? active = _refills.FindActiveRefillFor(person.EmployeeId);
        if (active == null)
        {
            Say(person.Name + " has no refill to answer.");
            return;
        }

        if (command == "yes")
        {
            AcceptResult result = _refills.Accept(active.Id, person.EmployeeId);
            if (result != AcceptResult.Accepted) Say("Sorry " + person.Name + ": " + result);
        }
        else if (command == "no")
        {
            _refills.Decline(active.Id, person.EmployeeId);
        }
        else if (command == "done" && w.Length >= 2 && decimal.TryParse(w[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
        {
            // The amount is in the box's office currency; items are the rest of the line, comma separated.
            Money money = Money.Of(amount, _settings.CurrencyOf(active.Office));
            // Optional receipt at the end: "done 850 chips, cola receipt:kiwi-0211.pdf".
            // In the real chat the photo/PDF is an attachment; here it is just a file name, and may be left out.
            string rest = string.Join(" ", w, 2, w.Length - 2);
            string receipt = "receipt-" + active.ShortId() + " (not attached in simulator)";
            int receiptAt = rest.IndexOf("receipt:", StringComparison.OrdinalIgnoreCase);
            if (receiptAt >= 0)
            {
                receipt = rest.Substring(receiptAt + 8).Trim();
                rest = rest.Substring(0, receiptAt);
            }
            List<string> items = new List<string>();
            foreach (string item in rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                items.Add(item);
            }
            FillResult result = _refills.MarkFilled(active.Id, person.EmployeeId, money, items, receipt);
            if (result != FillResult.Filled) Say("Sorry " + person.Name + ": " + result);
        }
        else
        {
            Say("Not understood. Type 'help'.");
        }
    }

    // Admin only. Changes apply at once and are saved to settings.json.
    //   set budget oslo 7000 300 1200     (monthly, typical min, typical max, in the office currency)
    //   set timer accept 45 | complete 24  (minutes to answer | hours to finish)
    private void HandleAdmin(Employee person, string[] w)
    {
        if (!person.IsAdmin)
        {
            Say("Sorry " + person.Name + ", only an admin can change settings.");
            return;
        }

        string? error = null;
        if (w.Length == 6 && w[1] == "budget" && Enum.TryParse(w[2], true, out Office office)
            && TryDecimal(w[3], out decimal monthly) && TryDecimal(w[4], out decimal min) && TryDecimal(w[5], out decimal max))
        {
            error = _budget.UpdateBudget(office, monthly, min, max);
            if (error == null) Say(office + " budget is now " + monthly + " " + _settings.CurrencyOf(office) + ", typical " + min + "-" + max + ".");
        }
        else if (w.Length == 4 && w[1] == "timer" && int.TryParse(w[3], out int value) && value > 0 && (w[2] == "accept" || w[2] == "complete"))
        {
            if (w[2] == "accept") _settings.Timers.AcceptWindow = TimeSpan.FromMinutes(value);
            else _settings.Timers.CompleteWindow = TimeSpan.FromHours(value);
            Say("Timer '" + w[2] + "' is now " + value + (w[2] == "accept" ? " minutes." : " hours.") + " New offers use it.");
        }
        else
        {
            error = "Usage: set budget <office> <monthly> <min> <max> | set timer accept <minutes> | set timer complete <hours>";
        }

        if (error != null)
        {
            Say(error);
            return;
        }
        SettingsLoader.SaveToFile(_settingsPath, _settings);
    }

    private static bool TryDecimal(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    // hr approve C-1 | hr reject C-1 <reason> | hr down | hr claims
    private void HandleHr(string[] w)
    {
        string action = w.Length > 1 ? w[1].ToLowerInvariant() : "";
        if (action == "down")
        {
            _hr.FailNextSubmissions = 1;
            Say("HR will refuse the next claim.");
        }
        else if (action == "claims")
        {
            foreach (HrClaim c in _hr.Claims) Say(c.ClaimId + "  " + c.Request.EmployeeId + "  " + c.Request.Amount + "  " + c.Status);
        }
        else if ((action == "approve" || action == "reject") && w.Length > 2)
        {
            string claimId = w[2].ToUpperInvariant();
            bool approved = action == "approve";
            string reason = w.Length > 3 ? string.Join(" ", w, 3, w.Length - 3) : "";
            try
            {
                _hr.SetStatus(claimId, approved ? "Approved" : "Rejected");
                _refills.RecordHrDecision(claimId, approved, reason);
            }
            catch (KeyNotFoundException e)
            {
                Say(e.Message);
            }
        }
        else
        {
            Say("Usage: hr approve C-1 | hr reject C-1 <reason> | hr down | hr claims");
        }
    }

    // time +30m | +2h | +3d : moves the clock, then runs the sweeper (timeouts, HR retries).
    private void AdvanceTime(string[] w)
    {
        string text = w.Length > 1 ? w[1].TrimStart('+') : "";
        if (text.Length < 2 || !int.TryParse(text.Substring(0, text.Length - 1), out int n))
        {
            Say("Usage: time +30m | +2h | +3d");
            return;
        }
        char unit = text[text.Length - 1];
        TimeSpan amount = unit == 'm' ? TimeSpan.FromMinutes(n) : unit == 'h' ? TimeSpan.FromHours(n) : TimeSpan.FromDays(n);
        _clock.Advance(amount);
        Say("Time is now " + _clock.GetNow().ToString("ddd dd MMM HH:mm") + " UTC.");
        _refills.Tick();
    }

    private void PrintStatus()
    {
        foreach (Refill r in _refills.GetRefills())
        {
            Say(r.ShortId() + "  " + r.Office + "  " + r.State + "  " + (r.AssignedTo ?? "-") + (r.ClaimId == null ? "" : "  claim " + r.ClaimId));
        }
    }

    private Employee? FindPerson(string id)
    {
        foreach (Employee e in _people)
        {
            if (e.EmployeeId == id) return e;
        }
        return null;
    }

    private static int ParseInt(string[] w, int index, int fallback)
    {
        return w.Length > index && int.TryParse(w[index], out int value) ? value : fallback;
    }

    private static void Say(string text)
    {
        Console.WriteLine("  " + text);
    }

    private const string Help = @"<name>: empty | yes | no | done 850 chips, cola | away 5 | visiting oslo 10
  <admin>: set budget oslo 7000 300 1200 | set timer accept 45 | set timer complete 24
  hr approve C-1 | hr reject C-1 <reason> | hr down | hr claims
  time +30m | +2h | +3d     status     report     quit";
}
