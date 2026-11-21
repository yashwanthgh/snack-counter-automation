using SnackCounter;
using SnackCounter.HrSim;

namespace SnackCounter.Tests;

// HR claims: sent after the receipt, retried without duplicates, approved or rejected.
public class ClaimTests : TestBase
{
    [Test]
    public void Claim_is_sent_only_after_the_receipt_with_the_fields_HR_needs()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");
        Assert.That(Hr.Claims.Count, Is.EqualTo(0));

        service.MarkFilled(refill.Id, "anna", Money.Of(850, "NOK"), new List<string> { "chips", "cola" }, "receipt-123.jpg");

        ClaimRequest sent = Hr.Claims[0].Request;
        Assert.That(sent.EmployeeId, Is.EqualTo("anna"));
        Assert.That(sent.Amount.Minor, Is.EqualTo(85000));
        Assert.That(sent.Amount.Currency, Is.EqualTo("NOK"));
        Assert.That(sent.ReceiptImage, Is.EqualTo("receipt-123.jpg"));
        Assert.That(sent.Description, Does.Contain("chips, cola"));
        Assert.That(refill.State, Is.EqualTo(RefillState.Claimed));
    }

    [Test]
    public void When_HR_is_down_the_claim_is_retried_once_and_never_duplicated()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Hr.FailNextSubmissions = 1;

        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");
        service.MarkFilled(refill.Id, "anna", Money.Of(850, "NOK"), new List<string>(), "r.jpg");
        Assert.That(refill.State, Is.EqualTo(RefillState.Filled));

        service.Tick();
        service.Tick();

        Assert.That(refill.State, Is.EqualTo(RefillState.Claimed));
        Assert.That(Hr.Claims.Count, Is.EqualTo(1));
    }

    [Test]
    public void Same_idempotency_key_returns_the_same_claim()
    {
        ClaimRequest request = new ClaimRequest("anna", Money.Of(850, "NOK"), "snacks", "r.jpg", "refill-1");

        string first = Hr.SubmitClaim(request);
        string second = Hr.SubmitClaim(request);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(Hr.Claims.Count, Is.EqualTo(1));
    }

    [Test]
    public void Approved_claim_is_reimbursed_and_a_duplicate_decision_is_ignored()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = DoRefill(service, Office.Oslo, "anna", Money.Of(850, "NOK"));

        service.RecordHrDecision(refill.ClaimId!, true, "");
        service.RecordHrDecision(refill.ClaimId!, false, "sent twice");

        Assert.That(refill.State, Is.EqualTo(RefillState.Reimbursed));
        Assert.That(Chat.WasTold("anna", "approved"), Is.True);
    }

    [Test]
    public void Rejected_claim_tells_the_volunteer_and_the_contact()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateService();
        Refill refill = DoRefill(service, Office.Oslo, "anna", Money.Of(850, "NOK"));

        service.RecordHrDecision(refill.ClaimId!, false, "receipt unreadable");

        Assert.That(refill.State, Is.EqualTo(RefillState.ClaimRejected));
        Assert.That(Chat.WasTold("anna", "receipt unreadable"), Is.True);
        Assert.That(Chat.WasTold("contact:Oslo", "receipt unreadable"), Is.True);
        Assert.That(Budget.SpentInMonth(Office.Oslo, 2026, 11), Is.EqualTo(0)); // money given back to the budget
    }
}
