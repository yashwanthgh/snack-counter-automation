using SnackCounter;
using SnackCounter.ChatSim;
using SnackCounter.HrSim;

namespace SnackCounter.Web;

// Holds the whole simulated world (people, refills, budget, fake HR, clock) for the web pages.
// It reuses the terminal simulator's CommandHandler, so the web pages accept exactly the same commands.
// One world for everyone using the pages; a lock makes sure only one command runs at a time.
public class SimulatorHost
{
    private readonly object _lock = new object();

    private List<Employee> _people;
    private RefillService _refills;
    private BudgetService _budget;
    private FakeHr _hr;
    private SimClock _clock;
    private Settings _settings;
    private CommandHandler _handler;
    private ReportService _report;
    private List<ChatEntry> _log = new List<ChatEntry>();

    public SimulatorHost(string configFolder)
    {
        string settingsPath = Path.Combine(configFolder, "settings.json");
        _settings = SettingsLoader.LoadFromFile(settingsPath);
        _people = PeopleLoader.LoadFromFile(Path.Combine(configFolder, "people.json"));

        // Simulated time starts Monday 2 Nov 2026, 09:00 UTC (same as the terminal simulator).
        _clock = new SimClock(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        _hr = new FakeHr();
        _budget = new BudgetService(_settings.Budgets);
        _report = new ReportService(_budget);
        ReportService report = _report;
        _refills = new RefillService(_people, new WebChat(this), _clock, _hr, _budget, _settings.Timers);
        _handler = new CommandHandler(_people, _refills, _budget, report, _hr, _clock, _settings, settingsPath);
    }

    // Run one command, e.g. "anna: yes" or "time +1h".
    // The command handler prints its answers to the console, so we catch that text and add it to the chat.
    public void Send(string line)
    {
        line = line.Trim();
        if (line == "") return;

        lock (_lock)
        {
            string sender = "tester";
            int colon = line.IndexOf(':');
            if (colon > 0 && !line.Substring(0, colon).Contains(' ')) sender = line.Substring(0, colon).Trim().ToLowerInvariant();

            AddEntry("user", sender, "", line);

            TextWriter original = Console.Out;
            StringWriter captured = new StringWriter();
            Console.SetOut(captured);
            try
            {
                _handler.Handle(line);
            }
            finally
            {
                Console.SetOut(original);
            }

            string answer = captured.ToString().Trim();
            if (answer != "") AddEntry("system", "simulator", sender, answer);
        }
    }

    // Called by WebChat (inside Send, so already locked) and by Send itself.
    public void AddEntry(string kind, string from, string to, string text)
    {
        string time = _clock.GetNow().ToString("ddd dd MMM HH:mm");
        _log.Add(new ChatEntry(_log.Count + 1, time, kind, from, to, text));
    }

    // Everything the pages need, in one object (turned into JSON by the web server).
    public object GetState()
    {
        lock (_lock)
        {
            DateTimeOffset now = _clock.GetNow();

            // How many refills each person has finished (bought snacks for).
            List<Refill> allRefills = _refills.GetRefills();

            List<object> people = new List<object>();
            foreach (Employee e in _people)
            {
                int done = 0;
                foreach (Refill r in allRefills)
                {
                    if (r.AssignedTo == e.EmployeeId && r.Amount != null) done++;
                }
                bool away = !e.IsAvailable(now);
                bool visiting = e.CurrentOffice(now) != e.HomeOffice;

                people.Add(new
                {
                    id = e.EmployeeId,
                    name = e.Name,
                    homeOffice = e.HomeOffice.ToString(),
                    currentOffice = e.CurrentOffice(now).ToString(),
                    isVolunteer = e.IsVolunteer,
                    isAdmin = e.IsAdmin,
                    available = !away,
                    awayUntil = away && e.UnavailableUntil != null ? e.UnavailableUntil.Value.ToString("ddd dd MMM") : "",
                    visitingUntil = visiting && e.VisitingUntil != null ? e.VisitingUntil.Value.ToString("ddd dd MMM") : "",
                    refillsDone = done
                });
            }

            List<object> refills = new List<object>();
            foreach (Refill r in allRefills)
            {
                refills.Add(new
                {
                    id = r.ShortId(),
                    office = r.Office.ToString(),
                    state = r.State.ToString(),
                    assignedTo = r.AssignedTo ?? "-",
                    deadline = r.Deadline == null ? "-" : r.Deadline.Value.ToString("ddd dd MMM HH:mm"),
                    amount = r.Amount == null ? "-" : r.Amount.ToString(),
                    claimId = r.ClaimId ?? "-",
                    history = r.History
                });
            }

            List<object> budgets = new List<object>();
            foreach (OfficeBudget b in _settings.Budgets)
            {
                // Split what was spent: still waiting for HR, or already approved.
                long pending = 0;
                long approved = 0;
                foreach (Refill r in allRefills)
                {
                    if (r.Office != b.Office || r.Amount == null || r.FilledAt == null) continue;
                    if (r.FilledAt.Value.Year != now.Year || r.FilledAt.Value.Month != now.Month) continue;
                    if (r.State == RefillState.Reimbursed) approved += r.Amount.Minor;
                    else if (r.State == RefillState.Filled || r.State == RefillState.Claimed) pending += r.Amount.Minor;
                }
                budgets.Add(new
                {
                    office = b.Office.ToString(),
                    monthly = Money.FormatMinor(b.MonthlyBudgetMinor, b.Currency),
                    spent = Money.FormatMinor(_budget.SpentInMonth(b.Office, now.Year, now.Month), b.Currency),
                    remaining = _budget.RemainingText(b.Office, now),
                    pending = Money.FormatMinor(pending, b.Currency),
                    approved = Money.FormatMinor(approved, b.Currency),
                    typical = _budget.TypicalRange(b.Office)
                });
            }

            List<object> claims = new List<object>();
            foreach (HrClaim c in _hr.Claims)
            {
                claims.Add(new
                {
                    id = c.ClaimId,
                    employee = c.Request.EmployeeId,
                    amount = c.Request.Amount.ToString(),
                    status = c.Status,
                    receipt = c.Request.ReceiptImage,
                    description = c.Request.Description
                });
            }

            // This month's report, plus who spent what (from the spending ledger).
            MonthlyReport monthly = _report.Build(now.Year, now.Month, _settings.ReportCurrency, _settings.ReportRates);
            List<object> reportOffices = new List<object>();
            foreach (OfficeReportLine line in monthly.Offices)
            {
                reportOffices.Add(new
                {
                    office = line.Office.ToString(),
                    budget = Money.FormatMinor(line.BudgetMinor, line.Currency),
                    spent = Money.FormatMinor(line.SpentMinor, line.Currency),
                    remaining = Money.FormatMinor(line.BudgetMinor - line.SpentMinor, line.Currency),
                    refills = line.RefillCount
                });
            }
            List<object> purchases = new List<object>();
            foreach (SpendRecord record in _budget.RecordsInMonth(now.Year, now.Month))
            {
                purchases.Add(new
                {
                    when = record.At.ToString("ddd dd MMM HH:mm"),
                    employee = record.EmployeeId,
                    office = record.Office.ToString(),
                    amount = record.Amount.ToString(),
                    items = string.Join(", ", record.Items)
                });
            }
            object report = new
            {
                month = now.ToString("MMMM yyyy"),
                offices = reportOffices,
                combined = Money.FormatMinor(monthly.CombinedTotalMinor, monthly.CombinedCurrency),
                rates = monthly.RatesUsed,
                items = monthly.ItemCounts,
                purchases = purchases
            };

            return new
            {
                report = report,
                now = now.ToString("dddd dd MMM yyyy, HH:mm") + " UTC",
                acceptMinutes = _settings.Timers.AcceptWindow.TotalMinutes,
                completeHours = _settings.Timers.CompleteWindow.TotalHours,
                people = people,
                refills = refills,
                budgets = budgets,
                claims = claims,
                log = new List<ChatEntry>(_log)
            };
        }
    }
}
