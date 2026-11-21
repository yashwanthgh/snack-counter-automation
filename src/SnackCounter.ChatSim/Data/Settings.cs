using SnackCounter;

namespace SnackCounter.ChatSim;

// Everything read from config/settings.json.
public class Settings
{
    public SnackConfig Timers;
    public List<OfficeBudget> Budgets;

    // For the monthly report: the currency of the combined total, and how much 1 unit
    // of each currency is worth in it. In production these rates would come from finance.
    public string ReportCurrency;
    public Dictionary<string, decimal> ReportRates;

    public Settings(SnackConfig timers, List<OfficeBudget> budgets, string reportCurrency, Dictionary<string, decimal> reportRates)
    {
        Timers = timers;
        Budgets = budgets;
        ReportCurrency = reportCurrency;
        ReportRates = reportRates;
    }

    public string CurrencyOf(Office office)
    {
        foreach (OfficeBudget b in Budgets)
        {
            if (b.Office == office) return b.Currency;
        }
        throw new KeyNotFoundException("No budget for " + office);
    }
}
