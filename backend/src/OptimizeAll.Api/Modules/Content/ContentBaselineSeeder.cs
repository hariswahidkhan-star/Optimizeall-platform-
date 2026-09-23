using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Domain.Content;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Content;

/// <summary>
/// Baseline content every environment needs: onboarding checklist, FAQ and a welcome announcement.
/// Idempotent: rows are matched by onboarding key / FAQ question / announcement title and never duplicated or
/// overwritten, so admin edits made later through the CMS are preserved. The one exception is an onboarding action
/// link that still points at a retired baseline URL (<see cref="RetiredActionUrls"/>): it is moved to the current link.
/// </summary>
public sealed class ContentBaselineSeeder(TimeProvider clock) : ISeeder
{
    public string Profile => "Baseline";
    public int Order => 20;

    public const string WelcomeAnnouncementTitle = "Welcome to Optimize All";

    internal static readonly (string Key, string Title, string Description, string? ActionLabel, string? ActionUrl, OnboardingCompletionRule Rule)[] Steps =
    {
        ("verify-email", "Verify your email address",
            "Open the link we emailed you when you signed up. Verifying your email protects your account and is required before you can join campaigns or be paid.",
            null, null, OnboardingCompletionRule.EmailVerified),
        ("complete-profile", "Complete your profile",
            "Confirm your country and time zone and pick at least one interest. We use them to show you campaigns you can actually take part in and to display deadlines in your local time.",
            "Edit profile", AppLinks.Profile, OnboardingCompletionRule.ProfileCompleted),
        ("add-social-account", "Add a social media profile",
            "Add the established social profile you'll share from, with its creation date and follower count. One profile can only be registered to one Optimize All account.",
            "Add a profile", AppLinks.SocialAccounts, OnboardingCompletionRule.SocialAccountAdded),
        ("eligible-account", "Have a profile that qualifies",
            "Campaigns only accept established profiles. A newly created profile isn't eligible until it reaches the minimum account age shown on your Social profiles page; we'll tell you the exact date it qualifies.",
            "Check eligibility", AppLinks.SocialAccounts, OnboardingCompletionRule.EligibleSocialAccount),
        ("payout-details", "Add your payout details",
            "Tell us how you'd like to be paid (bank transfer, PayPal or mobile wallet). Your details are encrypted and only a masked hint is ever shown.",
            "Add payout details", AppLinks.PayoutDetails, OnboardingCompletionRule.PayoutProfileAdded),
        ("first-submission", "Share your first campaign post",
            "Pick a campaign you're eligible for, share the approved content with the required paid-partnership disclosure, then submit the public link to your post.",
            "Browse campaigns", AppLinks.Campaigns, OnboardingCompletionRule.FirstSubmission),
        ("first-approved", "Get your first post approved",
            "A reviewer checks every submission against the campaign rules. Once your post is approved, the reward is added to your earnings and paid in the next payout.",
            "Browse campaigns", AppLinks.Campaigns, OnboardingCompletionRule.FirstApprovedSubmission),
    };

    internal static readonly (string Category, string Question, string Answer)[] Faqs =
    {
        ("Getting started", "How does Optimize All work?",
            "Companies publish campaigns with approved content. You share that content from your own established social media profile, submit the link to your public post, and earn a fixed reward for every post a reviewer approves."),
        ("Getting started", "What do I need before I can join a campaign?",
            "A verified email address, at least one social media profile that meets the eligibility rules, and a campaign that targets your country, language and platform. Your homepage checklist shows exactly what's missing."),
        ("Eligibility", "Why isn't my new social media profile eligible?",
            "To protect advertisers from fake or throwaway accounts, a profile must have existed for a minimum number of days (set by the platform, and sometimes higher for a specific campaign) before it qualifies. Newly created profiles are shown as not yet eligible, with the date they will qualify."),
        ("Eligibility", "Can I register a profile that someone else already added?",
            "No. Each social profile can belong to only one Optimize All account. If a profile you own is already registered, contact support and we'll look into it."),
        ("Eligibility", "Do I need a minimum number of followers?",
            "Some campaigns set a minimum follower count or require a verified profile. The campaign page lists its requirements and tells you whether each of your profiles qualifies."),
        ("Submissions", "How do I prove that I shared a post?",
            "Submit the public link to your post. A screenshot can be attached as supporting evidence for the reviewer, but it is not proof on its own: the post must be publicly visible at the link you submit."),
        ("Submissions", "Why was my submission rejected or sent back for correction?",
            "Common reasons are a missing paid-partnership disclosure, a private or deleted post, the wrong content, or posting outside the campaign dates. The reviewer's reason is shown on the submission, and you can fix and resubmit when correction is allowed or appeal within the appeal window."),
        ("Submissions", "Can I delete my post after it's approved?",
            "No. Many campaigns require posts to stay live for a minimum time. Removing a post early can lead to the approval being reversed and the reward being deducted from future payouts."),
        ("Payments", "When do I get paid?",
            "Payouts run on a schedule, biweekly by default. Each payout includes your approved earnings that have passed the short holding period before the cutoff date. The exact next payout date is shown on your Earnings page."),
        ("Payments", "Why wasn't my balance paid out?",
            "Only approved earnings are paid, and only when your payable balance reaches the minimum payout threshold. Smaller balances carry over to the next payout. Earnings that are pending review or on hold are not included."),
        ("Payments", "How are my payout details protected?",
            "Your bank account, PayPal email or wallet number is encrypted when stored. Only a masked hint (for example ••••1234) is ever displayed, and only authorised finance staff can use the full details to pay you."),
        ("Disclosure & rules", "Do I have to say my post is sponsored?",
            "Yes. Paid-content disclosure is mandatory on every campaign post, using the platform's paid-partnership label or the disclosure text given in the campaign (for example #ad). Posts without a clear disclosure are rejected."),
        ("Disclosure & rules", "What isn't allowed?",
            "Buying followers or engagement, using fake or shared accounts, submitting someone else's post, editing the approved content in misleading ways, or submitting the same post twice. Violations can lead to rejected submissions, reversed earnings and account suspension."),
        ("Account", "How do I change my notification settings?",
            "Go to Settings → Notifications to choose which messages you receive by email or WhatsApp. Essential account and payment messages can't be turned off. In-app notifications are always available in your notification center."),
        ("Account", "How do I contact support?",
            "Open a support ticket from the Help page. You can link it to a specific submission or payout so we can help faster. We reply in the ticket and notify you by email."),
    };

    /// <summary>
    /// Action links earlier baseline versions seeded that no longer exist in the participant portal, by step key.
    /// Existing rows still pointing at one are moved to the current link; any other (admin-edited) link is kept.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> RetiredActionUrls = new Dictionary<string, string[]>
    {
        ["payout-details"] = ["/app/payout-details"],
    };

    public async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var existingSteps = await db.Set<OnboardingStep>().ToListAsync(ct);
        var existingKeys = existingSteps.Select(s => s.Key).ToHashSet();
        foreach (var step in existingSteps)
        {
            var baseline = Steps.FirstOrDefault(s => s.Key == step.Key);
            if (baseline.Key is not null && RetiredActionUrls.TryGetValue(step.Key, out var retired) &&
                step.ActionUrl is not null && retired.Contains(step.ActionUrl))
                step.ActionUrl = baseline.ActionUrl;
        }
        for (var i = 0; i < Steps.Length; i++)
        {
            var s = Steps[i];
            if (existingKeys.Contains(s.Key)) continue;
            db.Set<OnboardingStep>().Add(new OnboardingStep
            {
                Key = s.Key, Title = s.Title, Description = s.Description, ActionLabel = s.ActionLabel, ActionUrl = s.ActionUrl,
                CompletionRule = s.Rule, SortOrder = (i + 1) * 10, IsActive = true,
            });
        }

        var existingQuestions = (await db.Set<FaqItem>().Select(f => f.Question).ToListAsync(ct)).ToHashSet();
        for (var i = 0; i < Faqs.Length; i++)
        {
            var f = Faqs[i];
            if (existingQuestions.Contains(f.Question)) continue;
            db.Set<FaqItem>().Add(new FaqItem
            {
                Category = f.Category, Question = f.Question, Answer = f.Answer, SortOrder = (i + 1) * 10, IsPublished = true,
            });
        }

        if (!await db.Set<Announcement>().AnyAsync(a => a.Title == WelcomeAnnouncementTitle, ct))
        {
            db.Set<Announcement>().Add(new Announcement
            {
                Title = WelcomeAnnouncementTitle,
                Body = "Thanks for joining! Follow the checklist on your homepage to verify your email, add an established social " +
                       "profile and set up your payout details. Remember: every campaign post must clearly disclose that it is paid content.",
                Severity = AnnouncementSeverity.Info,
                Audience = ContentAudience.Onboarding,
                PublishAt = now,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
