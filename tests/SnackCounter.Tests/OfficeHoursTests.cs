using SnackCounter;

namespace SnackCounter.Tests;

// Opening hours: nobody is asked while the office is closed, and timers only count open time.
// Oslo in November is UTC+1, Bengaluru is always UTC+5:30.
public class OfficeHoursTests : TestBase
{
    private RefillService CreateServiceWithHours(TimeSpan completeWindow)
    {
        List<DayOfWeek> weekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        SnackConfig config = new SnackConfig(TimeSpan.FromMinutes(30), completeWindow, TimeSpan.FromDays(90));
        config.Hours.Add(new OfficeHours(Office.Oslo, TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo"), 10, 17, weekdays));
        config.Hours.Add(new OfficeHours(Office.Bengaluru, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), 10, 17, weekdays));
        return new RefillService(People, Chat, Clock, Hr, Budget, config);
    }

    private static DateTimeOffset Utc(int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.Zero);
    }

    [Test]
    public void Empty_at_night_waits_and_someone_is_asked_when_the_office_opens()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateServiceWithHours(TimeSpan.FromHours(3));
        Clock.Set(Utc(2, 19));                                   // Mon 20:00 in Oslo

        Refill refill = service.ReportEmpty(Office.Oslo, "lars");

        Assert.That(refill.State, Is.EqualTo(RefillState.Waiting));
        Assert.That(Chat.WasTold("anna", "refill"), Is.False);   // nobody pinged at night
        Assert.That(Chat.WasTold("lars", "office is closed"), Is.True);
        Assert.That(Chat.WasTold("lars", "Tue 03 Nov 09:00 UTC"), Is.True);   // 10:00 Oslo, shown in UTC

        Clock.Set(Utc(3, 8, 59));                                // Tue 09:59 in Oslo: still closed
        service.Tick();
        Assert.That(refill.State, Is.EqualTo(RefillState.Waiting));

        Clock.Set(Utc(3, 9));                                    // Tue 10:00 in Oslo: open
        service.Tick();
        Assert.That(refill.State, Is.EqualTo(RefillState.Offered));
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));
        Assert.That(refill.Deadline, Is.EqualTo(Utc(3, 9, 30)));  // 30 min from opening
    }

    [Test]
    public void Time_to_finish_pauses_overnight()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateServiceWithHours(TimeSpan.FromHours(3));
        Clock.Set(Utc(2, 15));                                   // Mon 16:00 in Oslo
        Refill refill = service.ReportEmpty(Office.Oslo, "x");
        service.Accept(refill.Id, "anna");

        // 1 hour on Monday (16-17) + 2 hours on Tuesday (10-12) = Tue 12:00 Oslo = 11:00 UTC.
        Assert.That(refill.Deadline, Is.EqualTo(Utc(3, 11)));

        Clock.Set(Utc(3, 8));                                    // Tue 09:00 Oslo: no hand-over overnight
        service.Tick();
        Assert.That(refill.State, Is.EqualTo(RefillState.Committed));
        Assert.That(refill.AssignedTo, Is.EqualTo("anna"));
    }

    [Test]
    public void Friday_evening_waits_until_monday_morning()
    {
        AddVolunteer("anna", Office.Oslo);
        RefillService service = CreateServiceWithHours(TimeSpan.FromHours(3));
        Clock.Set(Utc(6, 18));                                   // Fri 19:00 in Oslo

        Refill refill = service.ReportEmpty(Office.Oslo, "x");

        Assert.That(refill.Deadline, Is.EqualTo(Utc(9, 9)));     // Mon 10:00 in Oslo
    }

    [Test]
    public void Each_office_uses_its_own_time_zone()
    {
        AddVolunteer("anna", Office.Oslo);
        AddVolunteer("priya", Office.Bengaluru);
        RefillService service = CreateServiceWithHours(TimeSpan.FromHours(3));
        Clock.Set(Utc(2, 8));                                    // 09:00 Oslo (closed), 13:30 Bengaluru (open)

        Assert.That(service.ReportEmpty(Office.Oslo, "x").State, Is.EqualTo(RefillState.Waiting));
        Assert.That(service.ReportEmpty(Office.Bengaluru, "y").AssignedTo, Is.EqualTo("priya"));
    }
}
