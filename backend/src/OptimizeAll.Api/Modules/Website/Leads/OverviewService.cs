using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>KPIs for the agency portal's Website overview (inquiries, consultations, subscribers, blog, careers).</summary>
public sealed class OverviewService(AppDbContext db, TimeProvider clock)
{
    public async Task<WebsiteOverviewDto> GetAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-30);
        var previous = now.AddDays(-60);
        var recent = await db.Set<WebsiteInquiry>().AsNoTracking().Where(i => i.CreatedAt >= previous && i.Status != InquiryStatus.Spam)
            .Select(i => new { i.CreatedAt, i.Type, i.UtmSource }).ToListAsync(ct);
        var last30 = recent.Where(i => i.CreatedAt >= since).ToList();

        var byDay = Enumerable.Range(0, 30).Select(d => DateOnly.FromDateTime(since.AddDays(d + 1)))
            .Select(day => new CountByDto(day.ToString("yyyy-MM-dd"), last30.Count(i => DateOnly.FromDateTime(i.CreatedAt) == day))).ToList();

        return new WebsiteOverviewDto(
            last30.Count,
            recent.Count - last30.Count,
            await db.Set<WebsiteInquiry>().CountAsync(i => i.Status == InquiryStatus.New, ct),
            await db.Set<ConsultationBooking>().CountAsync(b => b.Status == BookingStatus.Confirmed && b.SlotStart >= now, ct),
            await db.Set<NewsletterSubscriber>().CountAsync(s => s.Status == NewsletterStatus.Confirmed, ct),
            await db.Set<NewsletterSubscriber>().CountAsync(s => s.Status == NewsletterStatus.Pending, ct),
            await db.Set<JobApplication>().CountAsync(a => a.Stage == ApplicationStage.New, ct),
            await db.Set<BlogPost>().CountAsync(p => p.Status == BlogPostStatus.Published, ct),
            await db.Set<BlogPost>().CountAsync(p => p.Status == BlogPostStatus.InReview, ct),
            last30.GroupBy(i => i.Type).Select(g => new CountByDto(g.Key.ToString(), g.Count())).OrderByDescending(x => x.Count).ToList(),
            last30.GroupBy(i => i.UtmSource ?? "(direct)").Select(g => new CountByDto(g.Key, g.Count())).OrderByDescending(x => x.Count).Take(8).ToList(),
            byDay);
    }
}
