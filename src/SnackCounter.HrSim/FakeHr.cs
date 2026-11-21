namespace SnackCounter.HrSim;

using SnackCounter;

// Fake HR system.
// - Stores claims and returns ids "C-1", "C-2", ...
// - Same idempotency key = same claim, so a retry never creates a second one.
// - FailNextSubmissions = N makes the next N calls fail (HR is down), to test retries.
public class FakeHr : IHr
{
    public List<HrClaim> Claims = new List<HrClaim>();
    public int FailNextSubmissions = 0;

    public string SubmitClaim(ClaimRequest request)
    {
        if (FailNextSubmissions > 0)
        {
            FailNextSubmissions--;
            throw new HrUnavailableException("HR system is not responding");
        }

        foreach (HrClaim existing in Claims)
        {
            if (existing.Request.IdempotencyKey == request.IdempotencyKey) return existing.ClaimId;
        }

        HrClaim claim = new HrClaim("C-" + (Claims.Count + 1), request);
        Claims.Add(claim);
        return claim.ClaimId;
    }

    public void SetStatus(string claimId, string status)
    {
        foreach (HrClaim c in Claims)
        {
            if (c.ClaimId == claimId)
            {
                c.Status = status;
                return;
            }
        }
        throw new KeyNotFoundException("No HR claim " + claimId);
    }
}
