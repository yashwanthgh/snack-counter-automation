using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.Tests;

// Budgets and totals across both currencies.
public class BudgetTests : TestBase
{
    [Test]
    public void Spending_counts_against_each_office_in_its_own_currency()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("priya", Office.Bengaluru);
        RefillService service = CreateService();

        DoRefill(service, Office.Oslo, "anna", Money.Of(850.50m, "NOK"));
        DoRefill(service, Office.Bengaluru, "priya", Money.Of(2400, "INR"));

        Assert.That(Budget.SpentInMonth(Office.Oslo, 2026, 11), Is.EqualTo(85050));         // 850.50 NOK
        Assert.That(Budget.RemainingMinor(Office.Oslo, Start), Is.EqualTo(514950));         // 5,149.50 NOK
        Assert.That(Budget.SpentInMonth(Office.Bengaluru, 2026, 11), Is.EqualTo(240000));   // 2,400 INR
    }

    [Test]
    public void Visitor_from_Oslo_buying_in_Bengaluru_is_charged_to_Bengaluru_in_INR()
    {
        Employee anna = AddVolunteer("anna", Office.Oslo);
        anna.VisitingOffice = Office.Bengaluru;
        anna.VisitingUntil = Start.AddDays(10);
        RefillService service = CreateService();

        DoRefill(service, Office.Bengaluru, "anna", Money.Of(1500, "INR"));

        Assert.That(Budget.SpentInMonth(Office.Bengaluru, 2026, 11), Is.EqualTo(150000));
        Assert.That(Budget.SpentInMonth(Office.Oslo, 2026, 11), Is.EqualTo(0));
        Assert.That(Hr.Claims[0].Request.Amount.Currency, Is.EqualTo("INR")); // HR pays her in NOK
    }

    [Test]
    public void Wrong_currency_or_zero_amount_is_refused()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");

        Assert.That(service.MarkFilled(refill.Id, "anna", Money.Of(500, "INR"), new List<string>(), "r.jpg"), Is.EqualTo(FillResult.WrongCurrency));
        Assert.That(service.MarkFilled(refill.Id, "anna", Money.Of(0, "NOK"), new List<string>(), "r.jpg"), Is.EqualTo(FillResult.InvalidAmount));
        Assert.That(service.MarkFilled(refill.Id, "anna", Money.Of(-500, "NOK"), new List<string>(), "r.jpg"), Is.EqualTo(FillResult.InvalidAmount));
        Assert.That(refill.State, Is.EqualTo(RefillState.Committed));
        Assert.That(Budget.SpentInMonth(Office.Oslo, 2026, 11), Is.EqualTo(0));
    }

    [Test]
    public void Low_budget_warns_the_contact_but_the_refill_goes_ahead()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        SpendEarlier(Office.Oslo, 4800.01m, "NOK"); // 1,199.99 left: less than one typical max purchase

        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        Assert.That(service.Accept(refill.Id, "anna"), Is.EqualTo(AcceptResult.Accepted));
        Assert.That(Chat.WasTold("contact:Oslo", "Budget low"), Is.True);
    }

    [Test]
    public void Exactly_one_typical_purchase_left_is_not_low()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        SpendEarlier(Office.Oslo, 4800, "NOK"); // exactly 1,200 left

        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");

        Assert.That(Chat.WasTold("contact:Oslo", "Budget low"), Is.False);
    }

    [Test]
    public void Buyer_is_told_the_limit_and_only_the_limit_is_claimed()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        SpendEarlier(Office.Oslo, 5000, "NOK"); // 1,000 left

        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");
        Assert.That(Chat.WasTold("anna", "spend up to 1,000.00 NOK"), Is.True);

        service.MarkFilled(refill.Id, "anna", Money.Of(1300, "NOK"), new List<string>(), "r.jpg");

        Assert.That(Hr.Claims[0].Request.Amount.Minor, Is.EqualTo(100000));        // 1,000 claimed, not 1,300
        Assert.That(Chat.WasTold("anna", "not paid back"), Is.True);
        Assert.That(Budget.RemainingMinor(Office.Oslo, Start), Is.EqualTo(0));     // never below zero
    }

    [Test]
    public void No_budget_left_means_nobody_buys_and_the_contact_decides()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        SpendEarlier(Office.Oslo, 6000, "NOK");

        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        Assert.That(service.Accept(refill.Id, "anna"), Is.EqualTo(AcceptResult.NoBudgetLeft));
        Assert.That(refill.State, Is.EqualTo(RefillState.Escalated));
        Assert.That(Chat.WasTold("anna", "don't buy"), Is.True);
        Assert.That(Chat.WasTold("contact:Oslo", "No budget left"), Is.True);
    }

    [Test]
    public void Budget_resets_each_month()
    {
        SpendEarlier(Office.Oslo, 5900, "NOK");
        DateTimeOffset december = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.That(Budget.RemainingMinor(Office.Oslo, december), Is.EqualTo(600000));
    }

    [Test]
    public void Admin_budget_change_applies_at_once_and_bad_values_are_refused()
    {
        SpendEarlier(Office.Oslo, 1000, "NOK");

        Assert.That(Budget.UpdateBudget(Office.Oslo, 8000, 300, 1500), Is.Null);
        Assert.That(Budget.RemainingMinor(Office.Oslo, Start), Is.EqualTo(700000)); // 8,000 - 1,000 already spent

        Assert.That(Budget.UpdateBudget(Office.Oslo, 8000, 1500, 300), Is.Not.Null);  // min > max
        Assert.That(Budget.UpdateBudget(Office.Oslo, 1000, 300, 1500), Is.Not.Null);  // max > monthly
        Assert.That(Budget.UpdateBudget(Office.Oslo, 8000, 0, 1500), Is.Not.Null);    // min not above 0
        Assert.That(Budget.GetBudget(Office.Oslo).MonthlyBudgetMinor, Is.EqualTo(800000)); // unchanged by refusals
    }
}
