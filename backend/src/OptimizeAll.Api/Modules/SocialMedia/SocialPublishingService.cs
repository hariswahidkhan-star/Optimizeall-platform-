using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed record PublishRunSummary(int Claimed, int Published, int Failed, int Retrying, int Recovered);

/// <summary>
/// Publishes due posts. Exactly-once is guaranteed by two conditional updates: a post is claimed with
/// <c>UPDATE … SET Status=Publishing WHERE Id=@id AND Status=Scheduled</c>, then each variant with
/// <c>… SET PublishStatus=Publishing WHERE Id=@id AND PublishStatus=Pending</c> before its adapter is called. Concurrent
/// runs, other instances and retries lose the race and skip. A variant whose outcome is unknown (the process died between
/// the provider call and saving the result) is never re-sent automatically: stale claims are recovered as
/// Failed/Unknown so a person verifies on the network first.
/// </summary>
public sealed class SocialPublishingService(
    AppDbContext db,
    SocialPublisherRegistry publishers,
    ProfileTokenStore tokens,
    ICredentialVault vault,
    SocialAppCredentials appCredentials,
    SocialOAuthRegistry oauth,
    IOptions<SocialMediaOptions> options,
    INotificationService notifications,
    IAuditLogger audit,
    TimeProvider clock,
    ILogger<SocialPublishingService> logger)
{
    public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(15);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PublishRunSummary> RunAsync(int batchSize, CancellationToken ct)
    {
        var recovered = await RecoverStaleAsync(ct);
        var now = Now;
        var due = await db.Set<SocialPost>().AsNoTracking()
            .Where(p => p.Status == SocialPostStatus.Scheduled && p.ScheduledAt != null && p.ScheduledAt <= now
                        && (p.NextAttemptAt == null || p.NextAttemptAt <= now))
            .OrderBy(p => p.ScheduledAt).Select(p => p.Id).Take(batchSize).ToListAsync(ct);

        int claimed = 0, published = 0, failed = 0, retrying = 0;
        foreach (var id in due)
        {
            var outcome = await PublishPostAsync(id, ct);
            if (outcome is null) continue;
            claimed++;
            switch (outcome)
            {
                case SocialPostStatus.Published: published++; break;
                case SocialPostStatus.Scheduled: retrying++; break;
                default: failed++; break;
            }
        }
        return new PublishRunSummary(claimed, published, failed, retrying, recovered);
    }

    /// <summary>Publishes one post if this caller wins the claim; returns the resulting status or null when not claimed.</summary>
    public async Task<SocialPostStatus?> PublishPostAsync(Guid postId, CancellationToken ct)
    {
        var claimId = Guid.NewGuid();
        var now = Now;
        var won = await db.Set<SocialPost>()
            .Where(p => p.Id == postId && p.Status == SocialPostStatus.Scheduled && p.ScheduledAt <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, SocialPostStatus.Publishing)
                .SetProperty(p => p.PublishClaimId, claimId)
                .SetProperty(p => p.PublishClaimedAt, now)
                .SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid()), ct);
        if (won == 0) return null;

        db.ChangeTracker.Clear();
        var post = await db.Set<SocialPost>().AsNoTracking().Include(p => p.Variants).FirstAsync(p => p.Id == postId, ct);
        var profiles = await db.Set<BrandProfile>().Where(p => p.ClientAccountId == post.ClientAccountId).ToDictionaryAsync(p => p.Id, ct);
        var campaign = post.CampaignId is null ? null
            : await db.Set<SocialCampaign>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == post.CampaignId, ct);
        var settings = await db.Set<SocialClientSettings>().AsNoTracking().FirstOrDefaultAsync(s => s.ClientAccountId == post.ClientAccountId, ct)
                       ?? new SocialClientSettings { ClientAccountId = post.ClientAccountId };
        var mediaIds = post.Variants.SelectMany(v => v.MediaIds).Distinct().ToList();
        var media = await db.Set<SocialMediaAsset>().AsNoTracking().Where(m => mediaIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var maxAttempts = Math.Max(1, options.Value.MaxPublishAttempts);

        foreach (var variant in post.Variants.Where(v => v.PublishStatus == VariantPublishStatus.Pending
                                                         && (v.NextAttemptAt == null || v.NextAttemptAt <= now)))
        {
            var attemptNumber = variant.Attempts + 1;
            var claimedVariant = await db.Set<SocialPostVariant>()
                .Where(v => v.Id == variant.Id && v.PublishStatus == VariantPublishStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.PublishStatus, VariantPublishStatus.Publishing)
                    .SetProperty(v => v.Attempts, attemptNumber), ct);
            if (claimedVariant == 0) continue;

            var started = Now;
            var profile = profiles.GetValueOrDefault(variant.ProfileId);
            PublishResult result;
            Guid? connectionId = null;
            if (profile is null || !profile.IsActive)
            {
                result = PublishResult.Fail(PublishFailureKind.Rejected, "The profile no longer exists or is archived.");
            }
            else
            {
                var token = profile.ConnectionStatus == ProfileConnectionStatus.Connected ? await tokens.ReadAsync(profile, ct) : null;
                if (token is not null && token.RefreshToken is not null && token.ExpiresAt is { } expires && expires <= Now.AddMinutes(2))
                    token = await RefreshAsync(profile, token, ct);
                connectionId = token?.ConnectionId;
                var request = BuildRequest(post, variant, profile, token?.AccessToken, campaign, settings, media);
                try
                {
                    result = await publishers.For(variant.Network).PublishAsync(request, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Publisher for {Network} threw for variant {Variant}", variant.Network, variant.Id);
                    result = PublishResult.Fail(PublishFailureKind.Unknown,
                        $"The {PostValidator.Label(variant.Network)} adapter failed unexpectedly ({ex.GetType().Name}); verify on the network before retrying.");
                }
            }
            // A "published" answer without a provider id is not proof of publication.
            if (result.Outcome == PublishOutcome.Published && string.IsNullOrWhiteSpace(result.ExternalPostId))
                result = PublishResult.Fail(PublishFailureKind.Unknown, "The provider did not return a post id; verify on the network.");

            await RecordAsync(post, variant, attemptNumber, result, started, maxAttempts, ct);
            if (result.FailureKind == PublishFailureKind.Authorization && profile is not null)
                await MarkAuthorizationErrorAsync(profile, connectionId, result.Message, ct);
        }

        return await FinishAsync(postId, claimId, ct);
    }

    /// <summary>Refreshes an expiring token (X issues two-hour tokens with a refresh token). Returns null when it cannot.</summary>
    private async Task<StoredToken?> RefreshAsync(BrandProfile profile, StoredToken token, CancellationToken ct)
    {
        var app = await appCredentials.GetAsync(profile.Network, ct);
        if (app is null) return token;
        var refreshed = await oauth.For(profile.Network).RefreshAsync(profile, app, token.RefreshToken!, ct);
        if (!refreshed.Success || refreshed.AccessToken is null)
        {
            logger.LogWarning("Token refresh for profile {Profile} failed: {Error}", profile.Id, refreshed.Error);
            return token;
        }
        var tracked = await db.Set<BrandProfile>().FirstAsync(p => p.Id == profile.Id, ct);
        await tokens.StoreAsync(tracked, refreshed.AccessToken, refreshed.RefreshToken ?? token.RefreshToken, refreshed.ExpiresAt, null, ct);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new StoredToken(token.ConnectionId, refreshed.AccessToken, refreshed.RefreshToken ?? token.RefreshToken, refreshed.ExpiresAt);
    }

    private PublishRequest BuildRequest(SocialPost post, SocialPostVariant v, BrandProfile profile, string? token, SocialCampaign? campaign,
        SocialClientSettings settings, IReadOnlyDictionary<Guid, SocialMediaAsset> media)
    {
        var rules = NetworkPresets.Default(v.Network);
        var link = SocialPostService.EffectiveLink(v.Link, post.AutoAppendUtm, v.Network, campaign, settings, post.Title);
        var text = PostValidator.ComposeText(v.Text, v.Hashtags, link, rules.LinkHandling);
        var items = v.MediaIds.Select((id, i) => media.TryGetValue(id, out var m)
            ? new PublishMedia(m.Kind, PublicUrl(m), i < v.AltTexts.Count && v.AltTexts[i].Length > 0 ? v.AltTexts[i] : m.AltText)
            : new PublishMedia(MediaKind.Image, null, null)).ToList();
        return new PublishRequest(v.Id, v.Network, profile.Handle, profile.ExternalId, token, text, v.Title,
            rules.LinkHandling == LinkHandling.InText ? null : link, v.FirstComment, items);
    }

    /// <summary>Absolute URL the network can fetch: an https URL, or a public upload served by GET /api/v1/files/{id}.</summary>
    public string? PublicUrl(SocialMediaAsset m)
    {
        if (!string.IsNullOrWhiteSpace(m.ExternalUrl)) return m.ExternalUrl;
        var baseUrl = options.Value.PublicApiBaseUrl;
        if (m.FileId is null || !m.IsPublic || string.IsNullOrWhiteSpace(baseUrl)) return null;
        return baseUrl.TrimEnd('/') + FileUrls.For(m.FileId.Value);
    }

    private async Task RecordAsync(SocialPost post, SocialPostVariant variant, int attempt, PublishResult result, DateTime started,
        int maxAttempts, CancellationToken ct)
    {
        var finished = Now;
        VariantPublishStatus status;
        DateTime? nextAttempt = null;
        if (result.Outcome == PublishOutcome.Published)
        {
            status = VariantPublishStatus.Published;
        }
        else if (result.FailureKind == PublishFailureKind.Transient && PostWorkflow.RetryDelay(attempt, maxAttempts) is { } delay)
        {
            status = VariantPublishStatus.Pending;
            nextAttempt = finished.Add(delay);
        }
        else
        {
            status = VariantPublishStatus.Failed;
        }

        var message = result.Message is { Length: > 2000 } m ? m[..2000] : result.Message;
        await db.Set<SocialPostVariant>()
            .Where(v => v.Id == variant.Id && v.PublishStatus == VariantPublishStatus.Publishing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.PublishStatus, status)
                .SetProperty(v => v.NextAttemptAt, nextAttempt)
                .SetProperty(v => v.FailureKind, result.FailureKind)
                .SetProperty(v => v.FailureReason, status == VariantPublishStatus.Published ? result.Message : message)
                .SetProperty(v => v.ExternalPostId, result.ExternalPostId)
                .SetProperty(v => v.PublishedUrl, result.Url)
                .SetProperty(v => v.PublishedAt, status == VariantPublishStatus.Published ? finished : (DateTime?)null), ct);

        db.Set<SocialPublishAttempt>().Add(new SocialPublishAttempt
        {
            PostId = post.Id, VariantId = variant.Id, ClientAccountId = post.ClientAccountId, Network = variant.Network,
            AttemptNumber = attempt, Outcome = result.Outcome.ToString(), FailureKind = result.FailureKind, Message = message,
            ExternalPostId = result.ExternalPostId, StartedAt = started, FinishedAt = finished,
        });
        audit.RecordSystem(status == VariantPublishStatus.Published ? "social.variant.published" : "social.variant.publish_failed",
            nameof(SocialPostVariant), variant.Id, new { result.Outcome, result.FailureKind, result.ExternalPostId, result.Url, attempt });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task MarkAuthorizationErrorAsync(BrandProfile profile, Guid? connectionId, string? message, CancellationToken ct)
    {
        if (connectionId is { } id) await vault.MarkStatusAsync(id, IntegrationStatus.Error, message, ct);
        var trimmed = message is { Length: > 1000 } ? message[..1000] : message;
        await db.Set<BrandProfile>().Where(p => p.Id == profile.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ConnectionStatus, ProfileConnectionStatus.Error)
                .SetProperty(p => p.StatusMessage, trimmed)
                .SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid()), ct);
    }

    private async Task<SocialPostStatus> FinishAsync(Guid postId, Guid claimId, CancellationToken ct)
    {
        var variants = await db.Set<SocialPostVariant>().AsNoTracking().Where(v => v.PostId == postId).ToListAsync(ct);
        var retryAt = variants.Where(v => v.PublishStatus == VariantPublishStatus.Pending).Select(v => v.NextAttemptAt).Min();
        var anyRetry = variants.Any(v => v.PublishStatus == VariantPublishStatus.Pending);
        var status = PostWorkflow.Aggregate(variants.Select(v => v.PublishStatus).ToList(), anyRetry);
        var publishedAt = variants.Where(v => v.PublishedAt != null).Select(v => v.PublishedAt).Min();
        var failures = variants.Where(v => v.PublishStatus == VariantPublishStatus.Failed)
            .Select(v => $"{PostValidator.Label(v.Network)}: {v.FailureReason}").ToList();
        var reason = failures.Count == 0 ? null : string.Join(" | ", failures);
        if (reason is { Length: > 2000 }) reason = reason[..2000];

        var updated = await db.Set<SocialPost>()
            .Where(p => p.Id == postId && p.PublishClaimId == claimId && p.Status == SocialPostStatus.Publishing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.NextAttemptAt, status == SocialPostStatus.Scheduled ? retryAt : null)
                .SetProperty(p => p.PublishedAt, status == SocialPostStatus.Published ? publishedAt : null)
                .SetProperty(p => p.FailureReason, status == SocialPostStatus.Published ? null : reason)
                .SetProperty(p => p.PublishClaimId, (Guid?)null)
                .SetProperty(p => p.ConcurrencyStamp, Guid.NewGuid()), ct);

        if (updated == 1 && status is SocialPostStatus.Published or SocialPostStatus.Failed)
        {
            var post = await db.Set<SocialPost>().AsNoTracking().FirstAsync(p => p.Id == postId, ct);
            db.Set<SocialPostComment>().Add(new SocialPostComment
            {
                PostId = postId, ClientAccountId = post.ClientAccountId, AuthorName = "Publishing",
                Kind = status == SocialPostStatus.Published ? PostCommentKind.Published : PostCommentKind.Failed,
                Body = status == SocialPostStatus.Published ? "Published on every network." : $"Publishing failed. {reason}",
                IsInternal = status == SocialPostStatus.Failed, CreatedAt = Now,
            });
            if (status == SocialPostStatus.Failed)
                await notifications.StageAsync(new NotificationRequest(post.CreatedByUserId, "social.publish_failed",
                    $"Post failed to publish: {post.Title}", reason ?? "See the publishing log.", SocialLinks.Post(postId)), ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return status;
    }

    /// <summary>Claims older than <see cref="StaleClaim"/>: in-flight variants become Failed/Unknown (never re-sent automatically).</summary>
    public async Task<int> RecoverStaleAsync(CancellationToken ct)
    {
        var cutoff = Now - StaleClaim;
        var stale = await db.Set<SocialPost>().AsNoTracking()
            .Where(p => p.Status == SocialPostStatus.Publishing && p.PublishClaimedAt < cutoff)
            .OrderBy(p => p.PublishClaimedAt).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.PublishClaimId }).Take(50).ToListAsync(ct);
        foreach (var s in stale)
        {
            await db.Set<SocialPostVariant>()
                .Where(v => v.PostId == s.Id && v.PublishStatus == VariantPublishStatus.Publishing)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(v => v.PublishStatus, VariantPublishStatus.Failed)
                    .SetProperty(v => v.FailureKind, PublishFailureKind.Unknown)
                    .SetProperty(v => v.FailureReason,
                        "Publishing was interrupted after the request was sent. Check the network: if the post is live, mark it as published; otherwise retry."), ct);
            if (s.PublishClaimId is { } claim) await FinishAsync(s.Id, claim, ct);
        }
        return stale.Count;
    }
}

/// <summary>Publishes due scheduled posts every minute.</summary>
public sealed class SocialPublishingJob(SocialPublishingService service) : IJob
{
    public string Name => nameof(SocialPublishingJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var s = await service.RunAsync(25, ct);
        return $"claimed {s.Claimed}, published {s.Published}, failed {s.Failed}, retrying {s.Retrying}, recovered {s.Recovered}";
    }
}

/// <summary>
/// Re-queues evergreen posts: for each published evergreen original whose latest publication is older than its interval
/// and which has repeats left, creates a copy (Scheduled at the next hour) with a unique (original, repeat number), and
/// bumps the repeat count with a conditional update. Retries and parallel runs cannot create a second copy.
/// </summary>
public sealed class SocialEvergreenJob(AppDbContext db, IDatabaseDialect dialect, IAuditLogger audit, TimeProvider clock) : IJob
{
    public string Name => nameof(SocialEvergreenJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await db.Set<SocialPost>().AsNoTracking()
            .Where(p => p.IsEvergreen && p.RecycledFromPostId == null && p.Status == SocialPostStatus.Published
                        && p.EvergreenRepeatCount < p.EvergreenMaxRepeats)
            .OrderBy(p => p.PublishedAt).ThenBy(p => p.Id)
            .Include(p => p.Variants).Take(200).ToListAsync(ct);
        var created = 0;
        foreach (var original in candidates)
        {
            var copies = await db.Set<SocialPost>().AsNoTracking().Where(p => p.RecycledFromPostId == original.Id)
                .Select(p => new { p.Status, p.PublishedAt }).ToListAsync(ct);
            if (copies.Any(c => c.Status is not (SocialPostStatus.Published or SocialPostStatus.Failed))) continue; // one pending copy at a time
            var lastPublished = copies.Select(c => c.PublishedAt).Append(original.PublishedAt).Max();
            var repeat = EvergreenRules.DueRepeat(original.IsEvergreen, original.EvergreenRepeatCount, original.EvergreenMaxRepeats,
                original.EvergreenIntervalDays, lastPublished, now);
            if (repeat is null) continue;

            // The client approval gate also applies to recycled copies: when the client requires approval and never
            // approved the original (e.g. the setting was switched on later), the copy waits for the client.
            var requiresClient = await db.Set<SocialClientSettings>().AsNoTracking()
                .AnyAsync(x => x.ClientAccountId == original.ClientAccountId && x.RequireClientApproval, ct);
            var approved = !requiresClient
                           || await SocialPostService.IsClientApprovedAsync(db, original.ClientAccountId, original.ApprovedByUserId, ct);

            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            var bumped = await db.Set<SocialPost>()
                .Where(p => p.Id == original.Id && p.EvergreenRepeatCount == repeat - 1)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EvergreenRepeatCount, repeat.Value), ct);
            if (bumped == 0)
            {
                await tx.RollbackAsync(ct);
                continue;
            }
            var copy = new SocialPost
            {
                ClientAccountId = original.ClientAccountId,
                Title = $"{original.Title} (evergreen #{repeat})",
                Status = approved ? SocialPostStatus.Scheduled : SocialPostStatus.ClientApproval,
                ScheduledAt = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(1),
                CampaignId = original.CampaignId,
                AutoAppendUtm = original.AutoAppendUtm,
                RecycledFromPostId = original.Id,
                RecycleNumber = repeat,
                CreatedByUserId = original.CreatedByUserId,
                ApprovedByUserId = approved ? original.ApprovedByUserId : null,
                ApprovedAt = approved ? original.ApprovedAt : null,
            };
            foreach (var v in original.Variants)
            {
                copy.Variants.Add(new SocialPostVariant
                {
                    PostId = copy.Id, ClientAccountId = v.ClientAccountId, ProfileId = v.ProfileId, Network = v.Network, Text = v.Text,
                    Title = v.Title, MediaIds = v.MediaIds.ToList(), AltTexts = v.AltTexts.ToList(), Link = v.Link,
                    FirstComment = v.FirstComment, Hashtags = v.Hashtags.ToList(), Mentions = v.Mentions.ToList(),
                });
            }
            db.Set<SocialPost>().Add(copy);
            audit.RecordSystem("social.post.evergreen_requeued", nameof(SocialPost), copy.Id, new { original = original.Id, repeat });
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                created++;
            }
            catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
            {
                await tx.RollbackAsync(ct);
            }
            db.ChangeTracker.Clear();
        }
        return $"evergreen copies created: {created}";
    }
}
