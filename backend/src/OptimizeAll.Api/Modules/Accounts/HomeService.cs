using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Accounts;

public interface IHomeService
{
    Task<HomeDto> GetHomeAsync(Guid userId, CancellationToken ct);
    Task<OnboardingCompleteResponse> CompleteManualStepAsync(Guid userId, Guid stepId, CancellationToken ct);
}

public sealed class HomeService(AppDbContext db, IParticipantStateService state, TimeProvider clock) : IHomeService
{
    public async Task<HomeDto> GetHomeAsync(Guid userId, CancellationToken ct)
    {
        var s = await state.LoadAsync(userId, ct);
        var user = s.User;

        var homeState = !s.EmailVerified ? HomeState.VerifyEmail
            : !s.HasSocialAccount ? HomeState.AddSocialAccount
            : !s.HasEligibleAccount ? HomeState.AwaitingEligibility
            : s.SubmissionTotal == 0 ? HomeState.Ready
            : HomeState.Active;

        var onboarding = await BuildOnboardingAsync(s, ct);

        var banners = (await db.Set<HomepageBanner>().AsNoTracking().Where(b => b.IsActive)
                .OrderBy(b => b.SortOrder).ThenByDescending(b => b.CreatedAt).ToListAsync(ct))
            .Where(s.IsVisible)
            .Select(b => new BannerViewDto(b.Id, b.Title, b.Body, b.ImageUrl, b.CtaLabel, b.CtaUrl))
            .ToList();

        var announcements = await VisibleAnnouncementsAsync(db, s, ct);

        var unread = await db.Set<Notification>().CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
        var openTickets = await db.Set<SupportTicket>().CountAsync(t => t.UserId == userId &&
            t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed, ct);

        return new HomeDto(
            new HomeUserDto(user.Id, user.DisplayName, user.IsEmailVerified, user.Tier, user.TimeZone, user.CountryCode, user.LanguageCode),
            homeState,
            homeState == HomeState.AwaitingEligibility ? s.SoonestEligibleFrom : null,
            onboarding,
            banners,
            announcements,
            new SubmissionCountsDto(
                s.SubmissionTotal,
                s.Count(SubmissionStatus.Pending),
                s.Count(SubmissionStatus.UnderReview),
                s.Count(SubmissionStatus.Approved),
                s.Count(SubmissionStatus.NeedsCorrection),
                s.Count(SubmissionStatus.Rejected),
                s.Count(SubmissionStatus.Reversed)),
            unread,
            openTickets,
            new SocialAccountsSummaryDto(s.ActiveAccounts.Count, s.EligibleAccountCount, s.SoonestEligibleFrom, s.MinAccountAgeDays));
    }

    /// <summary>Active announcements for the user's audience, newest first.</summary>
    public static async Task<List<AnnouncementViewDto>> VisibleAnnouncementsAsync(AppDbContext db, ParticipantSnapshot s, CancellationToken ct)
    {
        var now = s.Now;
        return (await db.Set<Announcement>().AsNoTracking()
                .Where(a => a.IsActive && a.PublishAt <= now && (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.PublishAt).Take(50).ToListAsync(ct))
            .Where(s.IsVisible)
            .Select(a => new AnnouncementViewDto(a.Id, a.Title, a.Body, a.Severity, a.PublishAt, a.ExpiresAt))
            .ToList();
    }

    private async Task<OnboardingDto> BuildOnboardingAsync(ParticipantSnapshot s, CancellationToken ct)
    {
        var steps = await db.Set<OnboardingStep>().AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Key).ToListAsync(ct);
        var manualDone = (await db.Set<OnboardingStepCompletion>().AsNoTracking()
            .Where(c => c.UserId == s.User.Id).Select(c => c.StepId).ToListAsync(ct)).ToHashSet();

        var views = steps.Select(step => new OnboardingStepViewDto(
            step.Id, step.Key, step.Title, step.Description, step.ActionLabel, step.ActionUrl, step.CompletionRule,
            step.CompletionRule == OnboardingCompletionRule.Manual,
            IsCompleted(step, s, manualDone))).ToList();

        var completed = views.Count(v => v.Completed);
        var percent = views.Count == 0 ? 100 : (int)Math.Round(completed * 100.0 / views.Count, MidpointRounding.AwayFromZero);
        return new OnboardingDto(views, completed, views.Count, percent);
    }

    public static bool IsCompleted(OnboardingStep step, ParticipantSnapshot s, IReadOnlySet<Guid> manualDone) => step.CompletionRule switch
    {
        OnboardingCompletionRule.EmailVerified => s.EmailVerified,
        OnboardingCompletionRule.ProfileCompleted => s.ProfileCompleted,
        OnboardingCompletionRule.SocialAccountAdded => s.HasSocialAccount,
        OnboardingCompletionRule.EligibleSocialAccount => s.HasEligibleAccount,
        OnboardingCompletionRule.PayoutProfileAdded => s.HasPayoutProfile,
        OnboardingCompletionRule.FirstSubmission => s.SubmissionTotal > 0,
        OnboardingCompletionRule.FirstApprovedSubmission => s.HasApprovedSubmission,
        OnboardingCompletionRule.Manual => manualDone.Contains(step.Id),
        _ => false,
    };

    public async Task<OnboardingCompleteResponse> CompleteManualStepAsync(Guid userId, Guid stepId, CancellationToken ct)
    {
        var step = await db.Set<OnboardingStep>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == stepId && x.IsActive, ct)
                   ?? throw DomainException.NotFound("OnboardingStep");
        if (step.CompletionRule != OnboardingCompletionRule.Manual)
            throw new DomainException("onboarding.not_manual",
                "This step completes automatically when you finish it; it can't be dismissed.");

        var existing = await db.Set<OnboardingStepCompletion>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.StepId == stepId, ct);
        if (existing is not null) return new OnboardingCompleteResponse(stepId, true, existing.CompletedAt);

        // Truncate to the column precision (microseconds) so this response matches later reads.
        var ticks = clock.GetUtcNow().UtcDateTime.Ticks;
        var now = new DateTime(ticks - ticks % 10, DateTimeKind.Utc);
        db.Set<OnboardingStepCompletion>().Add(new OnboardingStepCompletion { UserId = userId, StepId = stepId, CompletedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            // A concurrent request recorded it first: idempotent success.
            db.ChangeTracker.Clear();
            var row = await db.Set<OnboardingStepCompletion>().AsNoTracking().FirstAsync(c => c.UserId == userId && c.StepId == stepId, ct);
            return new OnboardingCompleteResponse(stepId, true, row.CompletedAt);
        }
        return new OnboardingCompleteResponse(stepId, true, now);
    }
}
