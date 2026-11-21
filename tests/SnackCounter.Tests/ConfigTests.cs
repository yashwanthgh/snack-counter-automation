using SnackCounter;
using SnackCounter.ChatSim;

namespace SnackCounter.Tests;

// The config files: the real ones load, and broken ones are refused with a clear message.
public class ConfigTests
{
    // Walk up from the test's bin folder until we find the repo's config folder.
    private static string ConfigFolder()
    {
        DirectoryInfo? dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "settings.json")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("config folder not found");
    }

    [Test]
    public void Real_settings_file_loads_with_the_budgets_from_the_brief()
    {
        Settings settings = SettingsLoader.LoadFromFile(Path.Combine(ConfigFolder(), "settings.json"));
        BudgetService budget = new BudgetService(settings.Budgets);

        Assert.That(budget.GetBudget(Office.Oslo).MonthlyBudgetMinor, Is.EqualTo(600000));
        Assert.That(budget.GetBudget(Office.Oslo).Currency, Is.EqualTo("NOK"));
        Assert.That(budget.GetBudget(Office.Bengaluru).TypicalMaxMinor, Is.EqualTo(300000));
        Assert.That(settings.Timers.AcceptWindow, Is.EqualTo(TimeSpan.FromMinutes(30)));
        Assert.That(settings.ReportRates["INR"], Is.EqualTo(0.13m));
    }

    [Test]
    public void Real_settings_have_office_hours_in_each_office_time_zone_and_bad_hours_are_refused()
    {
        Settings settings = SettingsLoader.LoadFromFile(Path.Combine(ConfigFolder(), "settings.json"));

        Assert.That(settings.Timers.Hours.Count, Is.EqualTo(2));
        Assert.That(settings.Timers.Hours[0].OpenHour, Is.EqualTo(10));
        Assert.That(settings.Timers.Hours[1].TimeZone.BaseUtcOffset, Is.EqualTo(new TimeSpan(5, 30, 0)));   // Bengaluru
        Assert.That(settings.Timers.Hours[0].WorkDays.Contains(DayOfWeek.Saturday), Is.False);

        string closesBeforeOpening = File.ReadAllText(Path.Combine(ConfigFolder(), "settings.json")).Replace("\"closeHour\": 17", "\"closeHour\": 9");
        Assert.Throws<InvalidDataException>(() => SettingsLoader.LoadFromJson(closesBeforeOpening));
    }

    [Test]
    public void Real_people_file_loads_volunteers_and_statuses()
    {
        List<Employee> people = PeopleLoader.LoadFromFile(Path.Combine(ConfigFolder(), "people.json"));
        DateTimeOffset monday = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        Employee? lars = null;
        Employee? ingrid = null;
        Employee? kavya = null;
        foreach (Employee e in people)
        {
            if (e.EmployeeId == "lars") lars = e;
            if (e.EmployeeId == "ingrid") ingrid = e;
            if (e.EmployeeId == "kavya") kavya = e;
        }

        Assert.That(lars!.IsVolunteer, Is.False);
        Assert.That(ingrid!.IsAvailable(monday), Is.False);
        Assert.That(kavya!.CurrentOffice(monday), Is.EqualTo(Office.Oslo));
    }

    [Test]
    public void Missing_budget_for_an_office_is_refused()
    {
        string json = @"{
          ""timers"": { ""acceptWindowMinutes"": 30, ""completeWindowHours"": 24, ""fairnessWindowDays"": 90 },
          ""budgets"": [ { ""office"": ""Oslo"", ""currency"": ""NOK"", ""monthlyBudget"": 6000, ""typicalPurchaseMin"": 300, ""typicalPurchaseMax"": 1200 } ],
          ""report"": { ""combinedCurrency"": ""NOK"", ""rates"": { ""NOK"": 1, ""INR"": 0.13 } }
        }";

        InvalidDataException e = Assert.Throws<InvalidDataException>(() => SettingsLoader.LoadFromJson(json))!;
        Assert.That(e.Message, Does.Contain("Bengaluru"));
    }

    [Test]
    public void Zero_or_negative_timer_is_refused()
    {
        string json = @"{
          ""timers"": { ""acceptWindowMinutes"": 0, ""completeWindowHours"": 24, ""fairnessWindowDays"": 90 },
          ""budgets"": [],
          ""report"": { ""combinedCurrency"": ""NOK"", ""rates"": { ""NOK"": 1 } }
        }";

        Assert.Throws<InvalidDataException>(() => SettingsLoader.LoadFromJson(json));
    }

    [Test]
    public void Duplicate_person_id_is_refused()
    {
        string json = @"[
          { ""id"": ""anna"", ""name"": ""Anna"", ""homeOffice"": ""Oslo"", ""isVolunteer"": true },
          { ""id"": ""anna"", ""name"": ""Anna 2"", ""homeOffice"": ""Oslo"", ""isVolunteer"": true }
        ]";

        Assert.Throws<InvalidDataException>(() => PeopleLoader.LoadFromJson(json));
    }

    [Test]
    public void Saved_settings_load_back_the_same()
    {
        Settings settings = SettingsLoader.LoadFromFile(Path.Combine(ConfigFolder(), "settings.json"));
        new BudgetService(settings.Budgets).UpdateBudget(Office.Oslo, 7000, 250, 1300);
        settings.Timers.AcceptWindow = TimeSpan.FromMinutes(45);

        string path = Path.Combine(Path.GetTempPath(), "snack-settings-" + Guid.NewGuid() + ".json");
        SettingsLoader.SaveToFile(path, settings);
        Settings loaded = SettingsLoader.LoadFromFile(path);
        File.Delete(path);

        Assert.That(loaded.Budgets[0].MonthlyBudgetMinor, Is.EqualTo(700000));
        Assert.That(loaded.Budgets[0].TypicalMinMinor, Is.EqualTo(25000));
        Assert.That(loaded.Timers.AcceptWindow, Is.EqualTo(TimeSpan.FromMinutes(45)));
        Assert.That(loaded.ReportRates["INR"], Is.EqualTo(0.13m));
    }
}
