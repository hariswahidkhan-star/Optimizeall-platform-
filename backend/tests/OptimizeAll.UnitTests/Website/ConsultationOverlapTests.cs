using OptimizeAll.Domain.Website;

namespace OptimizeAll.UnitTests.Website;

public sealed class ConsultationOverlapTests
{
    private static readonly DateTime Sunday = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_booking_blocks_slots_that_overlap_it_even_with_another_start_time()
    {
        // Booked 09:00–09:30 while slots were 30 minutes; the slot length is now 20 minutes (09:00, 09:20, 09:40).
        var settings = new ConsultationSettings
        {
            TimeZone = "UTC", SlotMinutes = 20, MinNoticeHours = 0, MaxDaysAhead = 30, IsEnabled = true,
            WeeklyAvailability = new() { new AvailabilityWindow(DayOfWeek.Monday, "09:00", "10:00") },
        };
        var start = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
        var booked = new[] { (start, start.AddMinutes(30)) };

        var slots = ConsultationSlots.Available(settings, TimeZoneInfo.Utc, new HashSet<DateOnly>(),
            new HashSet<string> { ConsultationBooking.KeyFor(start) }, Sunday, Sunday, Sunday.AddDays(2), booked);

        Assert.Equal(new[] { new DateTime(2026, 9, 21, 9, 40, 0, DateTimeKind.Utc) }, slots);
    }
}
