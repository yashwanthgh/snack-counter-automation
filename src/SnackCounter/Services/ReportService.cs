namespace SnackCounter;

// Builds the one monthly report management asked for.
public class ReportService
{
    private BudgetService _budget;

    public ReportService(BudgetService budget)
    {
        _budget = budget;
    }

    // rates: how much 1 unit of each currency is worth in combinedCurrency, e.g. { "NOK": 1, "INR": 0.13 }.
    public MonthlyReport Build(int year, int month, string combinedCurrency, Dictionary<string, decimal> rates)
    {
        MonthlyReport report = new MonthlyReport(year, month, combinedCurrency, rates);
        List<SpendRecord> records = _budget.RecordsInMonth(year, month);
        report.Purchases = records;

        foreach (Office office in Enum.GetValues<Office>())
        {
            OfficeBudget budget = _budget.GetBudget(office);
            OfficeReportLine line = new OfficeReportLine(office, budget.Currency, budget.MonthlyBudgetMinor);
            foreach (SpendRecord r in records)
            {
                if (r.Office != office) continue;
                line.SpentMinor += r.Amount.Minor;
                line.RefillCount++;
            }
            report.Offices.Add(line);

            // A missing rate must fail loudly, not count the office as zero.
            if (!rates.ContainsKey(budget.Currency)) throw new ArgumentException("No exchange rate given for " + budget.Currency);
            // Convert each office total once and round once; rounding every receipt would drift.
            report.CombinedTotalMinor += (long)Math.Round(line.SpentMinor * rates[budget.Currency], MidpointRounding.AwayFromZero);
        }

        // Which snacks get bought (and so, roughly, eaten). Case-insensitive.
        foreach (SpendRecord r in records)
        {
            foreach (string raw in r.Items)
            {
                string item = raw.Trim().ToLowerInvariant();
                if (item == "") continue;
                report.ItemCounts[item] = report.ItemCounts.GetValueOrDefault(item) + 1;
            }
        }
        return report;
    }
}
