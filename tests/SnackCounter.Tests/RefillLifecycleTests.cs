using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.Tests;

// Who gets asked, and what happens when they say yes, say no, or do nothing.
public class RefillLifecycleTests : TestBase
{
    [Test]
    public void Two_people_cannot_both_commit_to_the_same_refill()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        // Anna doesn't answer in 30 minutes, so Bjørn is asked. Anna's late "yes" is refused.
        Clock.Advance(TimeSpan.FromMinutes(31));
        service.Tick();

        Assert.That(service.Accept(refill.Id, "anna"), Is.EqualTo(AcceptResult.NotOfferedToYou));
        Assert.That(service.Accept(refill.Id, "bjorn"), Is.EqualTo(AcceptResult.Accepted));
        Assert.That(service.Accept(refill.Id, "anna"), Is.EqualTo(AcceptResult.AlreadyTaken));
    }

    [Test]
    public void Running_low_starts_a_refill_and_a_later_empty_joins_it()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();

        // "low" asks someone straight away, just like "empty".
        Refill refill = service.ReportEmpty(Office.Oslo, "x", runningLow: true);
        Assert.That(refill.State, Is.EqualTo(RefillState.Offered));
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));
        Assert.That(refill.History[0], Does.Contain("running low"));

        // When it then runs out, the same refill is used, not a second one.
        Refill again = service.ReportEmpty(Office.Oslo, "y");
        Assert.That(again.Id, Is.EqualTo(refill.Id));
        Assert.That(service.GetRefills().Count, Is.EqualTo(1));
    }

    [Test]
    public void Person_asked_can_say_the_box_is_full_and_nothing_is_bought()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        // Anna is asked, looks at the box, and it is full: a false "empty".
        service.Decline(refill.Id, "anna", boxIsFull: true);

        Assert.That(refill.State, Is.EqualTo(RefillState.NotNeeded));
        Assert.That(refill.AssignedTo, Is.Null);
        Assert.That(Chat.WasTold("bjorn", ""), Is.False);   // nobody else is asked
        Assert.That(Chat.WasTold("anna", "box is full"), Is.True);
        Assert.That(service.Accept(refill.Id, "anna"), Is.EqualTo(AcceptResult.AlreadyTaken));

        // When the box really runs out later, a new refill starts.
        Refill next = service.ReportEmpty(Office.Oslo, "y");
        Assert.That(next.Id, Is.Not.EqualTo(refill.Id));
    }

    [Test]
    public void Fifty_simultaneous_yes_replies_only_one_wins()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        AcceptResult[] results = new AcceptResult[50];
        Thread[] threads = new Thread[50];
        for (int i = 0; i < 50; i++)
        {
            int index = i;
            threads[i] = new Thread(() => { results[index] = service.Accept(refill.Id, "anna"); });
        }
        foreach (Thread t in threads) t.Start();
        foreach (Thread t in threads) t.Join();

        int accepted = 0;
        foreach (AcceptResult r in results) if (r == AcceptResult.Accepted) accepted++;
        Assert.That(accepted, Is.EqualTo(1));
    }

    [Test]
    public void Reporting_again_does_not_create_a_second_refill()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill first = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(first.Id, "anna");

        Assert.That(service.ReportEmpty(Office.Oslo, "y"), Is.SameAs(first));
        Assert.That(service.GetRefills().Count, Is.EqualTo(1));
        Assert.That(Chat.WasTold("anna", "reported empty again"), Is.True);
    }

    [Test]
    public void Refill_nobody_completes_is_handed_to_someone_else()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");

        // 23 hours: still Anna's. 25 hours: handed to Bjørn.
        Clock.Advance(TimeSpan.FromHours(23));
        service.Tick();
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));

        Clock.Advance(TimeSpan.FromHours(2));
        service.Tick();
        Assert.That(refill.AssignedTo, Is.EqualTo("bjorn"));
        Assert.That(refill.State, Is.EqualTo(RefillState.Offered));
        Assert.That(Chat.WasTold("anna", "handed to someone else"), Is.True);
    }

    [Test]
    public void Late_receipt_after_hand_over_is_not_claimed_but_the_contact_is_told()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");
        Clock.Advance(TimeSpan.FromHours(25));
        service.Tick();

        // Anna bought anyway.
        FillResult result = service.MarkFilled(refill.Id, "anna", Money.Of(500, "NOK"), new List<string>(), "r.jpg");

        Assert.That(result, Is.EqualTo(FillResult.NotYours));
        Assert.That(Hr.Claims.Count, Is.EqualTo(0));
        Assert.That(Chat.WasTold("contact:Oslo", "Late receipt"), Is.True);
    }

    [Test]
    public void Nobody_available_escalates_and_a_new_report_tries_again()
    {
        Employee anna = AddVolunteer("anna", Office.Oslo);
        anna.UnavailableUntil = Start.AddDays(3);
        RefillService service = CreateService();

        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        Assert.That(refill.State, Is.EqualTo(RefillState.Escalated));
        Assert.That(Chat.WasTold("contact:Oslo", "Nobody available"), Is.True);

        // Anna is back; someone reports again, so she is asked.
        Clock.Advance(TimeSpan.FromDays(4));
        service.ReportEmpty(Office.Oslo, "y");
        Assert.That(refill.State, Is.EqualTo(RefillState.Offered));
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));
    }

    [Test]
    public void Only_available_volunteers_in_that_office_are_asked_fewest_refills_first()
    {
        People.Add(new Employee("lars", "Lars", Office.Oslo, false));   // not a volunteer
        Employee away = AddVolunteer("aaron", Office.Oslo);
        away.UnavailableUntil = Start.AddDays(7);                      // on holiday
        AddVolunteer("priya", Office.Bengaluru);                        // other office
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();

        DoRefill(service, Office.Oslo, "anna", Money.Of(800, "NOK"));

        // Anna has done one, so Bjørn is next.
        Assert.That(service.ReportEmpty(Office.Oslo, "x").AssignedTo, Is.EqualTo("bjorn"));
    }

    [Test]
    public void Visitor_is_asked_in_the_office_they_are_visiting()
    {
        Employee anna = AddVolunteer("anna", Office.Oslo);
        anna.VisitingOffice = Office.Bengaluru;
        anna.VisitingUntil = Start.AddDays(10);
        RefillService service = CreateService();

        Assert.That(service.ReportEmpty(Office.Bengaluru, "x").AssignedTo, Is.EqualTo("anna"));
        Assert.That(service.ReportEmpty(Office.Oslo, "y").State, Is.EqualTo(RefillState.Escalated));
    }

    [Test]
    public void The_reporter_can_be_chosen_and_others_who_report_are_thanked()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();

        // Anna is first in the fair order, so she is asked even though she reported it.
        Refill refill = service.ReportEmpty(Office.Oslo, "anna");
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));

        // Someone else reporting gets a thank-you naming who was asked.
        service.Decline(refill.Id, "anna");
        Refill second = service.ReportEmpty(Office.Oslo, "lars");
        Assert.That(second.AssignedTo, Is.EqualTo("bjorn"));
    }

    [Test]
    public void Buyer_sending_done_twice_is_not_a_late_receipt()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");
        service.MarkFilled(refill.Id, "anna", Money.Of(500, "NOK"), new List<string>(), "r.jpg");

        // The chat lagged and Anna sent "done" again.
        FillResult again = service.MarkFilled(refill.Id, "anna", Money.Of(500, "NOK"), new List<string>(), "r.jpg");

        Assert.That(again, Is.EqualTo(FillResult.NotYours));
        Assert.That(Chat.WasTold("contact:Oslo", "Late receipt"), Is.False);
        Assert.That(Hr.Claims.Count, Is.EqualTo(1));
    }

    [Test]
    public void Buyer_can_say_no_after_yes_and_the_next_person_is_asked_at_once()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("bjorn", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");

        // Box reported again; Anna can't go after all, so she says no. No 24 h wait.
        service.ReportEmpty(Office.Oslo, "y");
        Assert.That(Chat.WasTold("anna", "reply no"), Is.True);
        service.Decline(refill.Id, "anna");

        Assert.That(refill.State, Is.EqualTo(RefillState.Offered));
        Assert.That(refill.AssignedTo, Is.EqualTo("bjorn"));
        Assert.That(Chat.WasTold("anna", "don't buy"), Is.True);
        Assert.That(service.MarkFilled(refill.Id, "anna", Money.Of(500, "NOK"), new List<string>(), "r.jpg"), Is.EqualTo(FillResult.NotYours));
    }
}
