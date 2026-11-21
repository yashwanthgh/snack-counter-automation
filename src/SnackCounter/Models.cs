namespace SnackCounter;

// All the small data types: offices, states, money, people, refills, budgets.
// No rules live here; the rules are in Services/.

// The two offices.
public enum Office
{
    Oslo,
    Bengaluru
}

// The steps a refill goes through:
//   Offered -> Committed -> Filled -> Claimed -> Reimbursed (or ClaimRejected)
//   no answer / no / not finished in time -> Offered to the next person, or Escalated if nobody is left
public enum RefillState
{
    Offered,        // one volunteer has been asked, waiting for yes/no
    Committed,      // volunteer said yes, now buying
    Filled,         // box refilled, receipt in; HR has not taken the claim yet
    Claimed,        // claim created in HR, waiting for approval (1-10 days)
    Reimbursed,     // HR approved; done
    ClaimRejected,  // HR rejected; volunteer and office contact told, handled by people
    Escalated,      // nobody available; office contact told
    Waiting,        // reported while the office is closed; someone is asked when it opens
    NotNeeded       // the person asked checked the box and it was full: nothing to buy
}

// The possible answers when someone says "yes".
public enum AcceptResult
{
    Accepted,
    NotOfferedToYou,
    AlreadyTaken,
    OfferExpired,
    NoBudgetLeft
}

// The possible answers when someone says "done" with a receipt.
public enum FillResult
{
    Filled,
    NotYours,
    InvalidAmount,
    WrongCurrency
}

// An amount of money.
// We store it as whole øre / paise (1/100 of a krone or rupee) so adding never has rounding errors.
// Example: 500.00 NOK is stored as Minor = 50000, Currency = "NOK".
public class Money
{
    public long Minor;
    public string Currency;

    public Money(long minor, string currency)
    {
        Minor = minor;
        Currency = currency;
    }

    // Helper: Money.Of(500, "NOK") means 500.00 NOK.
    public static Money Of(decimal amount, string currency)
    {
        long minor = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
        return new Money(minor, currency);
    }

    public override string ToString()
    {
        return FormatMinor(Minor, Currency);
    }

    public static string FormatMinor(long minor, string currency)
    {
        decimal amount = minor / 100m;
        return amount.ToString("#,0.00") + " " + currency;
    }
}

// Anyone who works at the company. Only volunteers are asked to refill,
// but anyone can report a box empty.
public class Employee
{
    public string EmployeeId;
    public string Name;
    public Office HomeOffice;
    public bool IsVolunteer;
    public bool IsAdmin;    // may change budgets and timers from chat

    // If set, the person is on holiday / not available until this time.
    public DateTimeOffset? UnavailableUntil;

    // If set, the person is visiting the other office until VisitingUntil.
    public Office? VisitingOffice;
    public DateTimeOffset? VisitingUntil;

    public Employee(string employeeId, string name, Office homeOffice, bool isVolunteer)
    {
        EmployeeId = employeeId;
        Name = name;
        HomeOffice = homeOffice;
        IsVolunteer = isVolunteer;
    }

    // Can we ask this person right now?
    public bool IsAvailable(DateTimeOffset now)
    {
        if (UnavailableUntil == null)
        {
            return true;
        }
        return UnavailableUntil.Value <= now;
    }

    // Which office is this person in right now?
    public Office CurrentOffice(DateTimeOffset now)
    {
        if (VisitingOffice != null)
        {
            bool visitStillGoing = VisitingUntil == null || VisitingUntil.Value > now;
            if (visitStillGoing)
            {
                return VisitingOffice.Value;
            }
        }
        return HomeOffice;
    }
}

// One "the box is empty" event, from report until reimbursed.
public class Refill
{
    public Guid Id;
    public Office Office;
    public DateTimeOffset ReportedAt;

    public RefillState State;
    public string? AssignedTo;        // who has it right now (null if nobody)
    public DateTimeOffset? Deadline;  // when the current step times out (null if no timer)

    // Set when someone says yes: the most the company pays back for this refill
    // (the budget left at that moment). Anything above it is the buyer's own cost.
    public long SpendLimitMinor;

    // Filled in when the box is refilled.
    public Money? Amount;             // what is claimed from HR (receipt, capped at the limit)
    public List<string> Items = new List<string>();
    public string? ReceiptImage;
    public DateTimeOffset? FilledAt;
    public SpendRecord? Spend;        // the budget entry, removed again if HR rejects the claim

    // HR claim.
    public string? ClaimId;

    // Everyone already asked for this refill, so we never ask the same person twice.
    public List<string> AlreadyAsked = new List<string>();

    // A log of everything that happened, for disputes and debugging.
    public List<string> History = new List<string>();

    public Refill(Office office, DateTimeOffset reportedAt)
    {
        Id = Guid.NewGuid();
        Office = office;
        ReportedAt = reportedAt;
    }

    // "Open" means the box is still empty and someone is (or should be) working on it.
    public bool IsOpen()
    {
        return State == RefillState.Offered
            || State == RefillState.Committed
            || State == RefillState.Escalated
            || State == RefillState.Waiting;
    }

    // Short id for chat messages, e.g. "R-3f2a".
    public string ShortId()
    {
        return "R-" + Id.ToString().Substring(0, 4);
    }
}

// "Person X did a refill at time T". Used only for fairness.
public class CompletedRefill
{
    public string EmployeeId;
    public DateTimeOffset At;

    public CompletedRefill(string employeeId, DateTimeOffset at)
    {
        EmployeeId = employeeId;
        At = at;
    }
}

// One purchase in the ledger. Keeps the original amount and currency from the receipt.
public class SpendRecord
{
    public Office Office;
    public string EmployeeId;
    public Money Amount;
    public List<string> Items;
    public DateTimeOffset At;

    public SpendRecord(Office office, string employeeId, Money amount, List<string> items, DateTimeOffset at)
    {
        Office = office;
        EmployeeId = employeeId;
        Amount = amount;
        Items = items;
        At = at;
    }
}

// The budget for one office. Always in that office's own currency.
public class OfficeBudget
{
    public Office Office;
    public string Currency;
    public long MonthlyBudgetMinor;
    public long TypicalMinMinor;
    public long TypicalMaxMinor;

    public OfficeBudget(Office office, string currency, decimal monthlyBudget, decimal typicalMin, decimal typicalMax)
    {
        Office = office;
        Currency = currency;
        MonthlyBudgetMinor = Money.Of(monthlyBudget, currency).Minor;
        TypicalMinMinor = Money.Of(typicalMin, currency).Minor;
        TypicalMaxMinor = Money.Of(typicalMax, currency).Minor;
    }
}

// What we send to the external HR system to create a reimbursement claim (fields from the brief).
public class ClaimRequest
{
    public string EmployeeId;
    public Money Amount;
    public string Description;
    public string ReceiptImage;

    // Same key = same claim. If we retry after a timeout, HR must not create a second claim.
    public string IdempotencyKey;

    public ClaimRequest(string employeeId, Money amount, string description, string receiptImage, string idempotencyKey)
    {
        EmployeeId = employeeId;
        Amount = amount;
        Description = description;
        ReceiptImage = receiptImage;
        IdempotencyKey = idempotencyKey;
    }
}

// Timers, read from config/settings.json (or set by a test). No values in the code.
public class SnackConfig
{
    public TimeSpan AcceptWindow;    // time to say yes/no
    public TimeSpan CompleteWindow;  // time to buy and refill after saying yes
    public TimeSpan FairnessWindow;  // how far back we count refills for fairness

    // When each office is open. The bot only asks people, and the timers only count, inside these hours.
    // An office with no entry is treated as always open (tests use this to keep time simple).
    public List<OfficeHours> Hours = new List<OfficeHours>();

    public SnackConfig(TimeSpan acceptWindow, TimeSpan completeWindow, TimeSpan fairnessWindow)
    {
        AcceptWindow = acceptWindow;
        CompleteWindow = completeWindow;
        FairnessWindow = fairnessWindow;
    }
}

// Opening hours of one office, in its own time zone, e.g. Oslo 10:00-17:00 Mon-Fri.
public class OfficeHours
{
    public Office Office;
    public TimeZoneInfo TimeZone;
    public int OpenHour;                 // 10 = 10:00
    public int CloseHour;                // 17 = 17:00
    public List<DayOfWeek> WorkDays;

    public OfficeHours(Office office, TimeZoneInfo timeZone, int openHour, int closeHour, List<DayOfWeek> workDays)
    {
        Office = office;
        TimeZone = timeZone;
        OpenHour = openHour;
        CloseHour = closeHour;
        WorkDays = workDays;
    }
}
