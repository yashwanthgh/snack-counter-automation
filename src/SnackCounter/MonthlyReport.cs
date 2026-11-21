namespace SnackCounter;

// The monthly report data and how it prints.

// The monthly report: each office in its own currency, plus one combined total.
// The rates are stored with the report so the combined number can always be reproduced.
public class MonthlyReport
{
    public int Year;
    public int Month;
    public string CombinedCurrency;
    public Dictionary<string, decimal> RatesUsed;
    public List<OfficeReportLine> Offices = new List<OfficeReportLine>();
    public long CombinedTotalMinor;
    public Dictionary<string, int> ItemCounts = new Dictionary<string, int>();
    public List<SpendRecord> Purchases = new List<SpendRecord>();   // every purchase, so totals can be checked

    public MonthlyReport(int year, int month, string combinedCurrency, Dictionary<string, decimal> rates)
    {
        Year = year;
        Month = month;
        CombinedCurrency = combinedCurrency;
        RatesUsed = new Dictionary<string, decimal>(rates);
    }

    public string ToText()
    {
        string text = "Snack spending report " + Year + "-" + Month.ToString("00") + "\n";
        foreach (OfficeReportLine line in Offices)
        {
            text += "  " + line.Office + ": spent " + Money.FormatMinor(line.SpentMinor, line.Currency)
                + " of " + Money.FormatMinor(line.BudgetMinor, line.Currency)
                + " (remaining " + Money.FormatMinor(line.BudgetMinor - line.SpentMinor, line.Currency)
                + ", " + line.RefillCount + " refills)\n";
        }
        text += "  Combined: " + Money.FormatMinor(CombinedTotalMinor, CombinedCurrency) + " (rates to " + CombinedCurrency + ":";
        foreach (KeyValuePair<string, decimal> rate in RatesUsed) text += " " + rate.Key + " " + rate.Value;
        text += ")\n  Items bought:";
        foreach (KeyValuePair<string, int> item in ItemCounts) text += " " + item.Key + " x" + item.Value + ";";
        text += "\n  Purchases:\n";
        if (Purchases.Count == 0) text += "    none\n";
        foreach (SpendRecord p in Purchases)
        {
            text += "    " + p.At.ToString("yyyy-MM-dd HH:mm") + " UTC  " + p.Office + "  " + p.EmployeeId + "  " + p.Amount
                + "  " + string.Join(", ", p.Items) + "\n";
        }
        return text;
    }
}

// One office's numbers for the month, in that office's own currency.
public class OfficeReportLine
{
    public Office Office;
    public string Currency;
    public long BudgetMinor;
    public long SpentMinor;
    public int RefillCount;

    public OfficeReportLine(Office office, string currency, long budgetMinor)
    {
        Office = office;
        Currency = currency;
        BudgetMinor = budgetMinor;
    }
}
