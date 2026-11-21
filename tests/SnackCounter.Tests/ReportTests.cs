using SnackCounter;

namespace SnackCounter.Tests;

// The monthly report: exact per office, one combined total with a given rate.
public class ReportTests : TestBase
{
    private static Dictionary<string, decimal> Rates()
    {
        return new Dictionary<string, decimal> { { "NOK", 1m }, { "INR", 0.13m } };
    }

    private void Spend(Office office, decimal amount, string currency, DateTimeOffset at, params string[] items)
    {
        Budget.Record(new SpendRecord(office, "someone", Money.Of(amount, currency), new List<string>(items), at));
    }

    [Test]
    public void Totals_are_exact_per_office_and_combined_with_the_given_rate()
    {
        Spend(Office.Oslo, 850.50m, "NOK", Start);
        Spend(Office.Oslo, 1199.99m, "NOK", Start);
        Spend(Office.Bengaluru, 2400m, "INR", Start);
        Spend(Office.Bengaluru, 999.99m, "INR", Start);

        MonthlyReport report = new ReportService(Budget).Build(2026, 11, "NOK", Rates());

        Assert.That(report.Offices[0].SpentMinor, Is.EqualTo(205049));   // 2,050.49 NOK
        Assert.That(report.Offices[1].SpentMinor, Is.EqualTo(339999));   // 3,399.99 INR
        Assert.That(report.Offices[1].RefillCount, Is.EqualTo(2));
        // 3,399.99 INR x 0.13 = 441.9987 NOK, rounded once to 442.00. Total 2,492.49 NOK.
        Assert.That(report.CombinedTotalMinor, Is.EqualTo(249249));
    }

    [Test]
    public void Only_the_requested_month_counts()
    {
        Spend(Office.Oslo, 500m, "NOK", new DateTimeOffset(2026, 10, 31, 23, 59, 0, TimeSpan.Zero));
        Spend(Office.Oslo, 700m, "NOK", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        Spend(Office.Oslo, 900m, "NOK", new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        MonthlyReport report = new ReportService(Budget).Build(2026, 11, "NOK", Rates());

        Assert.That(report.Offices[0].SpentMinor, Is.EqualTo(70000));
    }

    [Test]
    public void Items_are_counted_ignoring_case()
    {
        Spend(Office.Oslo, 500m, "NOK", Start, "Chips", "cola");
        Spend(Office.Bengaluru, 900m, "INR", Start, "chips");

        MonthlyReport report = new ReportService(Budget).Build(2026, 11, "NOK", Rates());

        Assert.That(report.ItemCounts["chips"], Is.EqualTo(2));
    }

    [Test]
    public void Missing_exchange_rate_is_an_error_not_a_silent_zero()
    {
        Spend(Office.Bengaluru, 900m, "INR", Start);
        Dictionary<string, decimal> onlyNok = new Dictionary<string, decimal> { { "NOK", 1m } };

        Assert.Throws<ArgumentException>(() => new ReportService(Budget).Build(2026, 11, "NOK", onlyNok));
    }

    [Test]
    public void Report_lists_every_purchase_with_who_how_much_and_what()
    {
        Budget.Record(new SpendRecord(Office.Oslo, "anna", Money.Of(850.50m, "NOK"), new List<string> { "chips", "cola" }, Start));

        string text = new ReportService(Budget).Build(2026, 11, "NOK", Rates()).ToText();

        Assert.That(text, Does.Contain("Oslo  anna  850.50 NOK  chips, cola"));
    }
}
