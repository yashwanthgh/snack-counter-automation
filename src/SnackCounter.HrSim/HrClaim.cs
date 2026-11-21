namespace SnackCounter.HrSim;

using SnackCounter;

// A claim as the fake HR system stores it. Status: "Pending", "Approved" or "Rejected".
public class HrClaim
{
    public string ClaimId;
    public ClaimRequest Request;
    public string Status = "Pending";

    public HrClaim(string claimId, ClaimRequest request)
    {
        ClaimId = claimId;
        Request = request;
    }
}
