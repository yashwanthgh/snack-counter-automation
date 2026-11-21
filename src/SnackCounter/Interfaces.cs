namespace SnackCounter;

// The outside world: chat, clock and HR.
// The service only talks to these interfaces, so tests and simulators can plug in fakes.

// The company chat tool. We only need to send messages.
// Replies ("yes", "no", "done") come back as calls to RefillService.Accept / Decline / MarkFilled.
public interface IChat
{
    void SendTo(string employeeId, string message);
    void SendToOfficeContact(Office office, string message);
}

// Tells the current time.
public interface IClock
{
    DateTimeOffset GetNow();
}

// The external HR system. Returns the HR claim id.
// HR tells us later whether it approved or rejected (RefillService.RecordHrDecision).
public interface IHr
{
    string SubmitClaim(ClaimRequest request);
}

// Thrown when the HR system cannot be reached. The claim is retried later.
public class HrUnavailableException : Exception
{
    public HrUnavailableException(string message) : base(message)
    {
    }
}
