using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Notifications.Templates;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>
/// "Book a consultation": staff-managed weekly availability, blackout days and slot length; visitors pick a free slot.
/// Double booking is impossible: the booking's <c>SlotKey</c> has a unique index, so of two concurrent requests for the same
/// slot exactly one commits and the other gets 409 <c>website.slot_taken</c>. Confirmations go out by email.
/// </summary>
public sealed class BookingService(
    AppDbContext db, IDatabaseDialect dialect, FormGuard guard, InquiryService inquiries, IEmailSender email, IOptions<EmailOptions> emailOptions,
    IAuditLogger audit, TimeProvider clock, ILogger<BookingService> logger, EmailTemplateService templates)
{
    public async Task<ConsultationSettings> SettingsAsync(CancellationToken ct)
    {
        var s = await db.Set<ConsultationSettings>().FirstOrDefaultAsync(x => x.Key == ConsultationSettings.DefaultKey, ct);
        if (s is not null) return s;
        s = Defaults();
        db.Set<ConsultationSettings>().Add(s);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            s = await db.Set<ConsultationSettings>().FirstAsync(x => x.Key == ConsultationSettings.DefaultKey, ct);
        }
        return s;
    }

    public static ConsultationSettings Defaults() => new()
    {
        TimeZone = "UTC",
        SlotMinutes = 30,
        MinNoticeHours = 12,
        MaxDaysAhead = 30,
        IsEnabled = true,
        WeeklyAvailability = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
            .SelectMany(d => new[] { new AvailabilityWindow(d, "09:00", "12:00"), new AvailabilityWindow(d, "13:00", "17:00") }).ToList(),
    };

    private static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    private async Task<IReadOnlyList<DateTime>> AvailableAsync(ConsultationSettings s, DateTime from, DateTime to, string? exceptKey, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var blackouts = (await db.Set<ConsultationBlackout>().AsNoTracking().Select(b => b.Date).ToListAsync(ct)).ToHashSet();
        var lower = from.AddDays(-1);
        var upper = to.AddDays(1);
        // Bookings that hold a slot (SlotKey set) and may overlap the window; slots can be up to a day long.
        var active = (await db.Set<ConsultationBooking>().AsNoTracking()
                .Where(b => b.SlotKey != null && b.SlotStart >= lower && b.SlotStart <= upper)
                .Select(b => new { Key = b.SlotKey!, b.SlotStart, b.SlotEnd }).ToListAsync(ct))
            .Where(b => b.Key != exceptKey).ToList();
        return ConsultationSlots.Available(s, Zone(s.TimeZone), blackouts, active.Select(b => b.Key).ToHashSet(), now, from, to,
            active.Select(b => (b.SlotStart, b.SlotEnd)).ToList());
    }

    // ---------------------------------------------------------------- Public

    public async Task<SlotsDto> SlotsAsync(DateOnly? fromDate, int days, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var from = fromDate is { } d ? d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-14) : now;
        if (from < now) from = now;
        var to = from.AddDays(Math.Clamp(days, 1, 62)).AddHours(28);
        var slots = s.IsEnabled ? await AvailableAsync(s, from, to, null, ct) : Array.Empty<DateTime>();
        return new SlotsDto(s.TimeZone, s.SlotMinutes, s.IsEnabled, slots);
    }

    public async Task<BookingConfirmationDto> BookAsync(BookConsultationInput input, CancellationToken ct)
    {
        if (guard.Check(input, ConsentTexts.FormVersion) == FormCheck.Spam)
            return new BookingConfirmationDto(LeadReference.For(Guid.NewGuid()), input.SlotStart ?? default, input.SlotStart ?? default, input.VisitorTimeZone,
                "Your consultation is booked. We've emailed you the details.");

        var e = new FieldErrors();
        var contact = InquiryService.ValidateContact(input, e);
        var tz = WebsiteRules.Clean(input.VisitorTimeZone) ?? "UTC";
        if (!FieldRules.IsTimeZone(tz)) e.Add("visitorTimeZone", "Unknown time zone.");
        var slugs = await inquiries.ServiceSlugsAsync(input.ServiceSlugs, e, required: false, ct);
        var slotStart = WebsiteRules.Utc(input.SlotStart);
        e.ThrowIfAny();

        var settings = await SettingsAsync(ct);
        if (!settings.IsEnabled) throw DomainException.Conflict("website.booking_disabled", "Online booking is paused. Please use the contact form instead.");
        var start = slotStart!.Value;
        var inquiry = inquiries.NewInquiry(InquiryType.Consultation, contact, input);
        inquiry.ServiceSlugs = slugs;
        inquiry.Message = WebsiteRules.Clean(input.Notes);
        var booking = new ConsultationBooking
        {
            SlotStart = start,
            SlotEnd = start.AddMinutes(settings.SlotMinutes),
            SlotKey = ConsultationBooking.KeyFor(start),
            Status = BookingStatus.Confirmed,
            Name = contact.Name,
            Email = contact.Email,
            Phone = contact.Phone,
            Company = contact.Company,
            Website = contact.Website,
            Notes = WebsiteRules.Clean(input.Notes),
            ServiceSlugs = slugs,
            VisitorTimeZone = tz,
            InquiryId = inquiry.Id,
        };

        // The unique SlotKey stops two bookings of the same start; the lock also stops overlapping bookings with different
        // starts (possible after the slot length changed) from both passing the availability check.
        await using (await LockSlotsAsync(ct))
        {
            var available = await AvailableAsync(settings, start.AddMinutes(-1), start.AddMinutes(1), null, ct);
            if (!available.Contains(start)) throw SlotTaken();

            await using (var tx = await dialect.BeginWriteTransactionAsync(db, ct))
            {
                db.Set<WebsiteInquiry>().Add(inquiry);
                db.Set<ConsultationBooking>().Add(booking);
                try
                {
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    db.ChangeTracker.Clear();
                    throw SlotTaken();
                }
            }
        }

        await inquiries.PublishAsync(inquiry, ct);
        await SendTemplateAsync(booking, EmailTemplateCatalog.BookingConfirmed, new(), ct);
        return new BookingConfirmationDto(LeadReference.For(booking.Id), booking.SlotStart, booking.SlotEnd, booking.VisitorTimeZone,
            "Your consultation is booked. We've emailed you the details.");
    }

    /// <summary>Serializes booking and rescheduling (taken before any write transaction).</summary>
    private async Task<IAsyncDisposable> LockSlotsAsync(CancellationToken ct)
    {
        try
        {
            return await dialect.AcquireNamedLockAsync(db, "consultation-slots", TimeSpan.FromSeconds(15), ct);
        }
        catch (TimeoutException)
        {
            throw DomainException.Conflict("website.booking_busy", "Lots of people are booking right now. Please try again in a moment.");
        }
    }

    private static DomainException SlotTaken() =>
        new("website.slot_taken", "Sorry, that time was just taken. Please pick another slot.", DomainErrorKind.Conflict);

    private static string Describe(ConsultationBooking b)
    {
        var zone = Zone(b.VisitorTimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(b.SlotStart, zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(b.SlotEnd, zone);
        return $"{local.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)}–{end.ToString("HH:mm", CultureInfo.InvariantCulture)} ({b.VisitorTimeZone})";
    }

    /// <summary>Sends an editable website email template (name, time and reference are always available).</summary>
    private async Task SendTemplateAsync(ConsultationBooking b, string key, Dictionary<string, string> values, CancellationToken ct)
    {
        values["name"] = b.Name;
        values["when"] = Describe(b);
        values["reference"] = LeadReference.For(b.Id);
        var mail = await templates.RenderAsync(key, values, ct);
        await SendAsync(b, mail.Subject, mail.Text, ct);
    }

    private async Task SendAsync(ConsultationBooking b, string subject, string text, CancellationToken ct)
    {
        try
        {
            var result = await email.SendAsync(new EmailMessage(b.Email, b.Name, subject, text), ct);
            if (!result.Success) logger.LogWarning("Consultation email for booking {BookingId} failed: {Error}", b.Id, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Consultation email for booking {BookingId} failed", b.Id);
        }
    }

    // ---------------------------------------------------------------- Staff (site.manage)

    public async Task<ConsultationSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var blackouts = await db.Set<ConsultationBlackout>().AsNoTracking().OrderBy(b => b.Date).ToListAsync(ct);
        return ToDto(s, blackouts);
    }

    public async Task<ConsultationSettingsDto> UpdateSettingsAsync(ConsultationSettingsInput input, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        CmsStore.CheckStamp(db, s, input.ConcurrencyStamp);
        var e = new FieldErrors();
        var tz = input.TimeZone.Trim();
        if (!FieldRules.IsTimeZone(tz)) e.Add("timeZone", "Pick a valid time zone, e.g. Europe/London.");
        var windows = new List<AvailabilityWindow>();
        var inputs = input.WeeklyAvailability ?? new List<AvailabilityWindowInput>();
        if (inputs.Count > 50) e.Add("weeklyAvailability", "Use at most 50 windows.");
        for (var i = 0; i < inputs.Count; i++)
        {
            var w = inputs[i];
            if (w.Day is not { } day || !Enum.IsDefined(day)) { e.Add($"weeklyAvailability[{i}].day", "Pick a day."); continue; }
            if (!ConsultationSlots.TryParseTime(w.Start, out var start) || !ConsultationSlots.TryParseTime(w.End, out var end))
            {
                e.Add($"weeklyAvailability[{i}]", "Use 24-hour times such as 09:00 and 17:30.");
                continue;
            }
            if (end - start < TimeSpan.FromMinutes(input.SlotMinutes)) { e.Add($"weeklyAvailability[{i}]", "Each window must fit at least one slot."); continue; }
            if (windows.Any(x => x.Day == day && ConsultationSlots.TryParseTime(x.Start, out var xs) && ConsultationSlots.TryParseTime(x.End, out var xe) && start < xe && xs < end))
            {
                e.Add($"weeklyAvailability[{i}]", "Windows on the same day must not overlap.");
                continue;
            }
            windows.Add(new AvailabilityWindow(day, w.Start, w.End));
        }
        e.ThrowIfAny();
        var before = new { s.TimeZone, s.SlotMinutes, s.MinNoticeHours, s.MaxDaysAhead, s.IsEnabled, s.WeeklyAvailability };
        s.TimeZone = tz;
        s.SlotMinutes = input.SlotMinutes;
        s.MinNoticeHours = input.MinNoticeHours;
        s.MaxDaysAhead = input.MaxDaysAhead;
        s.IsEnabled = input.IsEnabled;
        s.WeeklyAvailability = windows.OrderBy(w => w.Day).ThenBy(w => w.Start, StringComparer.Ordinal).ToList();
        audit.Record("website.booking_settings_updated", nameof(ConsultationSettings), s.Id, before,
            new { s.TimeZone, s.SlotMinutes, s.MinNoticeHours, s.MaxDaysAhead, s.IsEnabled, s.WeeklyAvailability });
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<BlackoutDto> AddBlackoutAsync(BlackoutInput input, CancellationToken ct)
    {
        var date = input.Date!.Value;
        if (await db.Set<ConsultationBlackout>().AnyAsync(b => b.Date == date, ct))
            throw DomainException.Conflict("website.blackout_exists", "That day is already blocked.");
        var b = new ConsultationBlackout { Date = date, Reason = WebsiteRules.Clean(input.Reason), CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.Add(b);
        audit.Record("website.blackout_added", nameof(ConsultationBlackout), b.Id, after: new { b.Date, b.Reason });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("website.blackout_exists", "That day is already blocked.");
        }
        return new BlackoutDto(b.Id, b.Date, b.Reason);
    }

    public async Task DeleteBlackoutAsync(Guid id, CancellationToken ct)
    {
        var b = await db.Set<ConsultationBlackout>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<ConsultationBlackout>();
        audit.Record("website.blackout_removed", nameof(ConsultationBlackout), b.Id, before: new { b.Date, b.Reason });
        db.Remove(b);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<BookingDto>> ListBookingsAsync(BookingQuery query, CancellationToken ct)
    {
        var q = db.Set<ConsultationBooking>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(b => b.Status == status);
        if (WebsiteRules.Utc(query.From) is { } from) q = q.Where(b => b.SlotStart >= from);
        if (WebsiteRules.Utc(query.To) is { } to) q = q.Where(b => b.SlotStart < to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(b => EF.Functions.Like(b.Name, p) || EF.Functions.Like(b.Email, p) || (b.Company != null && EF.Functions.Like(b.Company, p)));
        }
        return CmsStore.Map(await q.OrderBy(b => b.SlotStart).ToPagedAsync(query, ct), ToDto);
    }

    public async Task<BookingDto> CancelAsync(Guid id, CancelBookingInput input, CancellationToken ct)
    {
        var b = await db.Set<ConsultationBooking>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<ConsultationBooking>();
        CmsStore.CheckStamp(db, b, input.ConcurrencyStamp);
        if (b.Status != BookingStatus.Confirmed) throw DomainException.Conflict("website.booking_not_active", "Only confirmed bookings can be cancelled.");
        b.Status = BookingStatus.Cancelled;
        b.SlotKey = null; // frees the slot
        b.CancellationReason = input.Reason.Trim();
        b.CancelledAt = clock.GetUtcNow().UtcDateTime;
        audit.Record("website.booking_cancelled", nameof(ConsultationBooking), b.Id, after: new { b.SlotStart, b.Status }, reason: b.CancellationReason);
        await db.SaveChangesAsync(ct);
        if (input.NotifyVisitor)
            await SendTemplateAsync(b, EmailTemplateCatalog.BookingCancelled, new()
            {
                ["reason"] = b.CancellationReason ?? string.Empty,
                ["bookUrl"] = $"{emailOptions.Value.AppBaseUrl.TrimEnd('/')}/book-a-consultation",
            }, ct);
        return ToDto(b);
    }

    public async Task<BookingDto> RescheduleAsync(Guid id, RescheduleBookingInput input, CancellationToken ct)
    {
        var b = await db.Set<ConsultationBooking>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<ConsultationBooking>();
        CmsStore.CheckStamp(db, b, input.ConcurrencyStamp);
        if (b.Status != BookingStatus.Confirmed) throw DomainException.Conflict("website.booking_not_active", "Only confirmed bookings can be rescheduled.");
        var settings = await SettingsAsync(ct);
        var start = WebsiteRules.Utc(input.SlotStart)!.Value;
        await using (await LockSlotsAsync(ct))
        {
            // Staff may reschedule into any free slot of the published availability (minimum notice still applies).
            var available = await AvailableAsync(settings, start.AddMinutes(-1), start.AddMinutes(1), b.SlotKey, ct);
            if (!available.Contains(start)) throw SlotTaken();
            var before = new { b.SlotStart };
            b.SlotStart = start;
            b.SlotEnd = start.AddMinutes(settings.SlotMinutes);
            b.SlotKey = ConsultationBooking.KeyFor(start);
            audit.Record("website.booking_rescheduled", nameof(ConsultationBooking), b.Id, before, new { b.SlotStart });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                throw SlotTaken();
            }
        }
        if (input.NotifyVisitor)
            await SendTemplateAsync(b, EmailTemplateCatalog.BookingRescheduled, new(), ct);
        return ToDto(b);
    }

    public async Task<BookingDto> SetStatusAsync(Guid id, BookingStatusInput input, CancellationToken ct)
    {
        var b = await db.Set<ConsultationBooking>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw CmsStore.NotFound<ConsultationBooking>();
        CmsStore.CheckStamp(db, b, input.ConcurrencyStamp);
        if (input.Status is not (BookingStatus.Completed or BookingStatus.NoShow))
            throw FieldRules.FieldError("website.invalid", "status", "Mark the consultation as completed or no-show (use cancel to cancel).");
        if (b.Status == BookingStatus.Cancelled) throw DomainException.Conflict("website.booking_not_active", "This booking was cancelled.");
        var before = new { b.Status };
        b.Status = input.Status!.Value;
        audit.Record("website.booking_status_changed", nameof(ConsultationBooking), b.Id, before, new { b.Status });
        await db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    private static ConsultationSettingsDto ToDto(ConsultationSettings s, IEnumerable<ConsultationBlackout> blackouts) => new(
        s.TimeZone, s.SlotMinutes, s.MinNoticeHours, s.MaxDaysAhead, s.WeeklyAvailability, s.IsEnabled,
        blackouts.Select(b => new BlackoutDto(b.Id, b.Date, b.Reason)).ToList(), s.ConcurrencyStamp);

    private static BookingDto ToDto(ConsultationBooking b) => new(
        b.Id, LeadReference.For(b.Id), b.SlotStart, b.SlotEnd, b.Status, b.Name, b.Email, b.Phone, b.Company, b.Website, b.Notes, b.ServiceSlugs,
        b.VisitorTimeZone, b.InquiryId, b.CancellationReason, b.CancelledAt, b.CreatedAt, b.ConcurrencyStamp);
}
