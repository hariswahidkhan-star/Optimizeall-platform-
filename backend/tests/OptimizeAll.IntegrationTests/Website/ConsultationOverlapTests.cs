using System.Net.Http.Json;
using OptimizeAll.Domain.Identity;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Website;

/// <summary>
/// A booking blocks every slot that overlaps it, not only a slot with the same start time: after the slot length
/// changes, the new grid must not offer (or accept) a time that collides with an existing consultation.
/// </summary>
public sealed class ConsultationOverlapTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private async Task SettingsAsync(HttpClient admin, int slotMinutes)
    {
        var settings = await admin.GetJsonAsync("/api/v1/agency/website/bookings/settings");
        var everyDay = Enum.GetNames<DayOfWeek>().Select(d => new { day = d, start = "00:00", end = "24:00" }).ToArray();
        await admin.PutJsonAsync("/api/v1/agency/website/bookings/settings", new
        {
            timeZone = "UTC", slotMinutes, minNoticeHours = 1, maxDaysAhead = 14, isEnabled = true, weeklyAvailability = everyDay,
            concurrencyStamp = settings.GetProperty("concurrencyStamp").GetGuid(),
        });
    }

    [Fact]
    public async Task Changing_the_slot_length_never_offers_a_slot_that_overlaps_a_booking()
    {
        var admin = await api.AdminAsync();
        await SettingsAsync(admin, 30);
        var client = api.Anonymous();
        var slots = await client.GetJsonAsync("/api/v1/public/consultations/slots?days=3");
        var booked = slots.GetProperty("slots")[6].GetDateTime().ToUniversalTime();
        object Booking(string token, string name, DateTime at) => WebsiteTestKit.Form(token, new Dictionary<string, object?>
        {
            ["name"] = name, ["email"] = $"{name.ToLowerInvariant()}@example.test", ["slotStart"] = at, ["visitorTimeZone"] = "UTC",
        });
        (await client.PostAsJsonAsync("/api/v1/public/consultations", Booking(await api.FormTokenAsync(client), "Alice", booked))).EnsureSuccessStatusCode();

        // 20-minute slots now: the grid has starts inside Alice's 30-minute consultation.
        await SettingsAsync(admin, 20);
        var after = (await client.GetJsonAsync("/api/v1/public/consultations/slots?days=3")).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetDateTime().ToUniversalTime()).ToList();
        Assert.NotEmpty(after);
        Assert.DoesNotContain(after, t => t < booked.AddMinutes(30) && booked < t.AddMinutes(20));

        var overlapping = booked.Minute == 0 ? booked.AddMinutes(20) : booked.AddMinutes(10);
        var response = await client.PostAsJsonAsync("/api/v1/public/consultations", Booking(await api.FormTokenAsync(client), "Bruno", overlapping));
        await response.ShouldFailAsync(409, "website.slot_taken");
    }
}
