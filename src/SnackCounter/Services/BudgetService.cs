namespace SnackCounter;

// Keeps the spending ledger and answers budget questions.
//
// Rules:
//   - Spending counts against the office where the box is, in that office's currency.
//   - Budget is per calendar month, no carry-over.
//   - Checked when someone says "yes": is there room for a typical (max) purchase? If not, the contact is warned.
//   - Never blocked after buying: the volunteer has already paid.
//   - No budget left at "yes": nobody buys, the office contact decides (RefillService.Accept).
//   - Spending counts at "done", not at HR approval. If HR rejects, it is given back (Remove).
//
// shortcut: months are counted in UTC, so a Bengaluru purchase before 05:30 IST on the 1st counts in the
// previous month; use each office's time zone before real use.
public class BudgetService
{
    private List<OfficeBudget> _budgets;
    private List<SpendRecord> _ledger = new List<SpendRecord>();

    public BudgetService(List<OfficeBudget> budgets)
    {
        _budgets = budgets;
    }

    public OfficeBudget GetBudget(Office office)
    {
        foreach (OfficeBudget b in _budgets)
        {
            if (b.Office == office) return b;
        }
        throw new KeyNotFoundException("No budget for " + office);
    }

    // Admin changes an office's budget from chat. Returns an error message, or null if it worked.
    // Takes effect at once: "remaining" is always budget minus the ledger, so a mid-month change just works.
    public string? UpdateBudget(Office office, decimal monthlyBudget, decimal typicalMin, decimal typicalMax)
    {
        if (typicalMin <= 0 || typicalMin > typicalMax || typicalMax > monthlyBudget)
        {
            return "Need 0 < typical min <= typical max <= monthly budget.";
        }
        OfficeBudget b = GetBudget(office);
        b.MonthlyBudgetMinor = Money.Of(monthlyBudget, b.Currency).Minor;
        b.TypicalMinMinor = Money.Of(typicalMin, b.Currency).Minor;
        b.TypicalMaxMinor = Money.Of(typicalMax, b.Currency).Minor;
        return null;
    }

    public void Record(SpendRecord record)
    {
        _ledger.Add(record);
    }

    // HR rejected the claim, so the company never paid: give the money back to the budget.
    public void Remove(SpendRecord record)
    {
        _ledger.Remove(record);
    }

    // All purchases in a month, for the report.
    public List<SpendRecord> RecordsInMonth(int year, int month)
    {
        List<SpendRecord> result = new List<SpendRecord>();
        foreach (SpendRecord r in _ledger)
        {
            if (r.At.UtcDateTime.Year == year && r.At.UtcDateTime.Month == month) result.Add(r);
        }
        return result;
    }

    public long SpentInMonth(Office office, int year, int month)
    {
        long total = 0;
        foreach (SpendRecord r in RecordsInMonth(year, month))
        {
            if (r.Office == office) total += r.Amount.Minor;
        }
        return total;
    }

    // Budget left in the month of "now". Can be negative.
    public long RemainingMinor(Office office, DateTimeOffset now)
    {
        return GetBudget(office).MonthlyBudgetMinor - SpentInMonth(office, now.UtcDateTime.Year, now.UtcDateTime.Month);
    }

    public bool HasRoomForTypicalPurchase(Office office, DateTimeOffset now)
    {
        return RemainingMinor(office, now) >= GetBudget(office).TypicalMaxMinor;
    }

    public string RemainingText(Office office, DateTimeOffset now)
    {
        return Money.FormatMinor(RemainingMinor(office, now), GetBudget(office).Currency);
    }

    public string TypicalRange(Office office)
    {
        OfficeBudget b = GetBudget(office);
        return Money.FormatMinor(b.TypicalMinMinor, b.Currency) + " - " + Money.FormatMinor(b.TypicalMaxMinor, b.Currency);
    }
}
