using SnackCounter;

namespace SnackCounter.ChatSim;

// Simulated time: only moves when you type "time +30m".
public class SimClock : IClock
{
    private DateTimeOffset _now;

    public SimClock(DateTimeOffset start)
    {
        _now = start;
    }

    public DateTimeOffset GetNow()
    {
        return _now;
    }

    public void Advance(TimeSpan amount)
    {
        _now = _now + amount;
    }
}
