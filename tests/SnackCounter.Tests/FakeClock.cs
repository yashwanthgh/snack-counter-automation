using SnackCounter;

namespace SnackCounter.Tests;

// ---------------------------------------------------------------
// Simulated stand-ins for the clock, the chat tool and the HR system.
// Used by the tests and by the console chat simulator.
// ---------------------------------------------------------------

// Fake clock: time only moves when someone calls Advance.
public class FakeClock : IClock
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start)
    {
        _now = start;
    }

    public DateTimeOffset GetNow()
    {
        return _now;
    }

    // Jump straight to a moment (used by the opening-hours tests).
    public void Set(DateTimeOffset now)
    {
        _now = now;
    }

    public void Advance(TimeSpan amount)
    {
        _now = _now + amount;
    }
}
