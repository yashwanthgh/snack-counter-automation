namespace SnackCounter;

// Time maths for opening hours. "hours == null" means the office is always open.
//
// Example, Oslo 10:00-17:00 Mon-Fri, 3 hours to finish:
//   "yes" at Mon 16:00 -> 1 hour today + 2 hours Tuesday -> deadline Tue 12:00.
//   "empty" at Fri 20:00 -> nobody is asked until Mon 10:00.
public class WorkingHours
{
    // Is the office open at this moment?
    public static bool IsOpen(OfficeHours? hours, DateTimeOffset at)
    {
        if (hours == null) return true;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, hours.TimeZone);
        if (!hours.WorkDays.Contains(local.DayOfWeek)) return false;
        return local.Hour >= hours.OpenHour && local.Hour < hours.CloseHour;
    }

    // Now if open, otherwise the next opening time.
    public static DateTimeOffset NextOpen(OfficeHours? hours, DateTimeOffset at)
    {
        if (hours == null || IsOpen(hours, at)) return at;

        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, hours.TimeZone);
        DateTime day = local.Date;
        // Today's opening if it is still ahead, else try the following days (a week always has one).
        for (int i = 0; i < 8; i++)
        {
            if (hours.WorkDays.Contains(day.DayOfWeek))
            {
                DateTimeOffset opens = AtLocal(hours, day, hours.OpenHour);
                if (opens > at) return opens;
            }
            day = day.AddDays(1);
        }
        throw new InvalidOperationException("Office " + hours.Office + " has no work days");
    }

    // start + length, counting only time when the office is open.
    public static DateTimeOffset AddOpenTime(OfficeHours? hours, DateTimeOffset start, TimeSpan length)
    {
        if (hours == null) return start + length;

        DateTimeOffset t = NextOpen(hours, start);
        TimeSpan left = length;
        while (true)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(t, hours.TimeZone);
            DateTimeOffset closes = AtLocal(hours, local.Date, hours.CloseHour);
            TimeSpan openToday = closes - t;
            if (left <= openToday) return t + left;
            left -= openToday;
            t = NextOpen(hours, closes);
        }
    }

    // A wall-clock hour on a local date (e.g. 10:00 in Oslo), as an exact moment.
    private static DateTimeOffset AtLocal(OfficeHours hours, DateTime localDate, int hour)
    {
        DateTime wall = localDate.Date.AddHours(hour);
        // Kept in UTC like every other time in the system, so messages and comparisons agree.
        return new DateTimeOffset(wall, hours.TimeZone.GetUtcOffset(wall)).ToUniversalTime();
    }
}
