using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.Tests;

// Shared setup. Before each test: a fresh fake clock, chat, HR and budget.
// Tests set their own numbers (the ones from the brief), so editing config/settings.json
// can't silently change what a test proves.
public class TestBase
{
    // Monday 2 Nov 2026, 09:00 UTC.
    protected static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    protected FakeClock Clock = null!;
    protected FakeChat Chat = null!;
    protected FakeHr Hr = null!;
    protected BudgetService Budget = null!;
    protected List<Employee> People = null!;

    [SetUp]
    public void SetUpWorld()
    {
        Clock = new FakeClock(Start);
        Chat = new FakeChat();
        Hr = new FakeHr();
        List<OfficeBudget> budgets = new List<OfficeBudget>();
        budgets.Add(new OfficeBudget(Office.Oslo, "NOK", 6000, 300, 1200));
        budgets.Add(new OfficeBudget(Office.Bengaluru, "INR", 15000, 500, 3000));
        Budget = new BudgetService(budgets);
        People = new List<Employee>();
    }

    protected Employee AddVolunteer(string id, Office office)
    {
        Employee e = new Employee(id, id, office, true);
        People.Add(e);
        return e;
    }

    // 30 min to answer, 24 h to finish, 90-day fairness window.
    protected RefillService CreateService()
    {
        SnackConfig config = new SnackConfig(TimeSpan.FromMinutes(30), TimeSpan.FromHours(24), TimeSpan.FromDays(90));
        return new RefillService(People, Chat, Clock, Hr, Budget, config);
    }

    // Report, yes, done: one whole refill. Returns the refill (normally Claimed).
    protected Refill DoRefill(RefillService service, Office office, string volunteerId, Money amount)
    {
        Refill refill = service.ReportEmpty(office, "someone");
        Assert.That(refill.AssignedTo, Is.EqualTo(volunteerId));
        Assert.That(service.Accept(refill.Id, volunteerId), Is.EqualTo(AcceptResult.Accepted));
        Assert.That(service.MarkFilled(refill.Id, volunteerId, amount, new List<string> { "chips" }, "r.jpg"), Is.EqualTo(FillResult.Filled));
        return refill;
    }

    protected void SpendEarlier(Office office, decimal amount, string currency)
    {
        Budget.Record(new SpendRecord(office, "earlier", Money.Of(amount, currency), new List<string>(), Start));
    }
}
