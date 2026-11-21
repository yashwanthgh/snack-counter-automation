namespace SnackCounter;

// Decides who to ask next for a refill.
//
// Who can be asked:
//   - is a volunteer (regular employees can report, but are never asked to buy)
//   - available right now (not on holiday)
//   - in this office right now (visitors count)
//   - not already asked for this refill
//
// Who is asked first (fairness):
//   1. fewest refills done in the last 90 days
//   2. if tied: longest time since their last refill
//   3. if still tied: employee id in alphabetical order (so the result is always the same)
public class VolunteerSelector
{
    public static Employee? PickNext(List<Employee> employees, Office office, List<string> alreadyAsked,
        List<CompletedRefill> completedRefills, DateTimeOffset now, TimeSpan fairnessWindow)
    {
        Employee? best = null;
        int bestCount = 0;
        DateTimeOffset bestLast = DateTimeOffset.MinValue;

        foreach (Employee v in employees)
        {
            // Skip people who cannot be asked.
            if (!v.IsVolunteer || !v.IsAvailable(now) || v.CurrentOffice(now) != office) continue;
            if (alreadyAsked.Contains(v.EmployeeId)) continue;

            // Their refills in the window, and their last refill ever (MinValue = never, counts as longest ago).
            int count = 0;
            DateTimeOffset last = DateTimeOffset.MinValue;
            foreach (CompletedRefill c in completedRefills)
            {
                if (c.EmployeeId != v.EmployeeId) continue;
                if (c.At >= now - fairnessWindow) count++;
                if (c.At > last) last = c.At;
            }

            // Better than the best so far? Fewer refills, then longer ago, then id.
            bool better;
            if (best == null) better = true;
            else if (count != bestCount) better = count < bestCount;
            else if (last != bestLast) better = last < bestLast;
            else better = string.CompareOrdinal(v.EmployeeId, best.EmployeeId) < 0;

            if (better)
            {
                best = v;
                bestCount = count;
                bestLast = last;
            }
        }
        return best; // null means nobody can be asked
    }
}
