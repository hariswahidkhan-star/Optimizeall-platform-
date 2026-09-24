using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Projects;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>Onboarding checklist, brand kit and client feedback (CSAT/NPS).</summary>
public sealed partial class ClientRelationshipService(
    AppDbContext db,
    IClientScope scope,
    ICurrentUser currentUser,
    IAuditLogger audit,
    IDatabaseDialect dialect,
    DeliveryFileService files,
    TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ onboarding

    public async Task<OnboardingDto> OnboardingAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var rows = await db.Set<ClientOnboardingItem>().AsNoTracking().Where(i => i.ClientAccountId == clientId)
            .OrderBy(i => i.SortOrder).ToListAsync(ct);
        var completedBy = rows.Where(r => r.CompletedByUserId != null).Select(r => r.CompletedByUserId!.Value).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking()
            .Where(u => completedBy.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var items = rows.Select(i => new OnboardingItemDto(i.Id, i.Key, i.Title, i.Description, i.Category, i.Owner, i.SortOrder, i.Status,
            i.CompletedAt, i.CompletedByUserId is { } u && people.TryGetValue(u, out var n) ? n : null, i.Note, i.CompletedOnBehalfOfClient)).ToList();
        var applicable = items.Where(i => i.Status != OnboardingItemStatus.NotApplicable).ToList();
        var done = applicable.Count(i => i.Status == OnboardingItemStatus.Done);
        return new OnboardingDto(items, done, applicable.Count, applicable.Count == 0 ? 100 : (int)Math.Round(done * 100.0 / applicable.Count));
    }

    /// <summary>
    /// Staff (clients.manage) update any item's status, except that ticking or un-ticking a client-owned step goes through
    /// <see cref="SetOnBehalfOfClientAsync"/> (audited as done on the client's behalf). A client Owner may tick off items
    /// the client owns.
    /// </summary>
    public async Task<OnboardingDto> UpdateOnboardingItemAsync(Guid clientId, Guid itemId, UpdateOnboardingItemRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, scope.IsStaff ? ClientMemberRole.Viewer : ClientMemberRole.Owner, ct);
        var item = await db.Set<ClientOnboardingItem>().FirstOrDefaultAsync(i => i.Id == itemId && i.ClientAccountId == clientId, ct)
                   ?? throw DomainException.NotFound("OnboardingItem");
        var status = request.Status!.Value;
        if (!Enum.IsDefined(status)) throw DeliveryRules.Invalid("onboarding.invalid_status", "status", "Choose a status.");
        if (!scope.IsStaff && (item.Owner != OnboardingOwner.Client || status == OnboardingItemStatus.NotApplicable))
            throw DomainException.Forbidden("onboarding.agency_item", "Your account team completes this step.");
        if (scope.IsStaff && item.Owner == OnboardingOwner.Client
            && (status == OnboardingItemStatus.Done || (status == OnboardingItemStatus.Pending && item.Status == OnboardingItemStatus.Done)))
            throw DeliveryRules.Invalid("onboarding.client_item", "status",
                "This step is the client's. Mark it done (or not done) on the client's behalf instead.");
        var before = new { item.Status, item.Note };
        item.Status = status;
        item.Note = string.IsNullOrWhiteSpace(request.Note) ? item.Note : request.Note.Trim();
        item.CompletedAt = status == OnboardingItemStatus.Done ? Now : null;
        item.CompletedByUserId = status == OnboardingItemStatus.Done ? currentUser.Id : null;
        item.CompletedOnBehalfOfClient = false;
        audit.Record("client.onboarding_item_updated", nameof(ClientOnboardingItem), item.Id, before, new { item.Status, item.Note });
        await db.SaveChangesAsync(ct);
        return await OnboardingAsync(clientId, ct);
    }

    /// <summary>
    /// Staff mark a client-owned step done (or not done again) on the client's behalf, e.g. when the client confirmed by
    /// phone. Recorded who and when, flagged "completed by staff on behalf of client" (shown to the client) and audited.
    /// Idempotent: repeating the current state changes nothing.
    /// </summary>
    public async Task<OnboardingDto> SetOnBehalfOfClientAsync(Guid clientId, Guid itemId, OnBehalfOnboardingRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        if (!scope.IsStaff) throw DomainException.NotFound("OnboardingItem");
        var item = await db.Set<ClientOnboardingItem>().FirstOrDefaultAsync(i => i.Id == itemId && i.ClientAccountId == clientId, ct)
                   ?? throw DomainException.NotFound("OnboardingItem");
        if (item.Owner != OnboardingOwner.Client)
            throw DeliveryRules.Invalid("onboarding.not_client_item", "itemId", "This step is the agency's; set its status directly.");
        var done = request.Done!.Value;
        if (done == (item.Status == OnboardingItemStatus.Done)) return await OnboardingAsync(clientId, ct);
        var before = new { item.Status, item.CompletedAt, item.CompletedByUserId, item.CompletedOnBehalfOfClient, item.Note };
        item.Status = done ? OnboardingItemStatus.Done : OnboardingItemStatus.Pending;
        item.CompletedAt = done ? Now : null;
        item.CompletedByUserId = done ? currentUser.Id : null;
        item.CompletedOnBehalfOfClient = done;
        if (!string.IsNullOrWhiteSpace(request.Note)) item.Note = request.Note.Trim();
        audit.Record(done ? "client.onboarding_item_completed_on_behalf" : "client.onboarding_item_reopened_on_behalf",
            nameof(ClientOnboardingItem), item.Id, before,
            new { item.ClientAccountId, item.Title, item.Status, item.CompletedAt, item.CompletedByUserId, item.CompletedOnBehalfOfClient, item.Note },
            done ? "Completed by staff on behalf of client" : "Reopened by staff on behalf of client");
        await db.SaveChangesAsync(ct);
        return await OnboardingAsync(clientId, ct);
    }

    public async Task<OnboardingDto> EditOnboardingItemAsync(Guid clientId, Guid itemId, EditOnboardingItemRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var item = await db.Set<ClientOnboardingItem>().FirstOrDefaultAsync(i => i.Id == itemId && i.ClientAccountId == clientId, ct)
                   ?? throw DomainException.NotFound("OnboardingItem");
        if (!Enum.IsDefined(request.Owner)) throw DeliveryRules.Invalid("onboarding.invalid_owner", "owner", "Choose who completes this step.");
        var before = new { item.Title, item.Category, item.Owner, item.SortOrder };
        item.Title = request.Title.Trim();
        item.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        item.Category = string.IsNullOrWhiteSpace(request.Category) ? "Other" : request.Category.Trim();
        item.Owner = request.Owner;
        if (request.SortOrder is { } order) item.SortOrder = order;
        audit.Record("client.onboarding_item_edited", nameof(ClientOnboardingItem), item.Id, before, new { item.Title, item.Category, item.Owner, item.SortOrder });
        await db.SaveChangesAsync(ct);
        return await OnboardingAsync(clientId, ct);
    }

    /// <summary>Removes a step from this client's checklist (mark it Not applicable to keep it visible instead).</summary>
    public async Task<OnboardingDto> DeleteOnboardingItemAsync(Guid clientId, Guid itemId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var item = await db.Set<ClientOnboardingItem>().FirstOrDefaultAsync(i => i.Id == itemId && i.ClientAccountId == clientId, ct)
                   ?? throw DomainException.NotFound("OnboardingItem");
        db.Remove(item);
        audit.Record("client.onboarding_item_deleted", nameof(ClientOnboardingItem), item.Id, before: new { item.Title, item.Status });
        await db.SaveChangesAsync(ct);
        return await OnboardingAsync(clientId, ct);
    }

    public async Task<OnboardingDto> AddOnboardingItemAsync(Guid clientId, AddOnboardingItemRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var max = await db.Set<ClientOnboardingItem>().Where(i => i.ClientAccountId == clientId).MaxAsync(i => (int?)i.SortOrder, ct) ?? -1;
        var key = "custom-" + ClientService.NormalizeSlug(request.Title);
        if (key.Length > 56) key = key[..56];
        key += "-" + Guid.NewGuid().ToString("N")[..6];
        var item = new ClientOnboardingItem
        {
            ClientAccountId = clientId, Key = key, Title = request.Title.Trim(), Description = request.Description?.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "Other" : request.Category.Trim(), Owner = request.Owner, SortOrder = max + 1,
        };
        db.Set<ClientOnboardingItem>().Add(item);
        audit.Record("client.onboarding_item_added", nameof(ClientOnboardingItem), item.Id, after: new { item.Title, item.Owner });
        await db.SaveChangesAsync(ct);
        return await OnboardingAsync(clientId, ct);
    }

    // ------------------------------------------------------------------ brand kit

    public async Task<BrandKitDto> BrandKitAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var kit = await EnsureKitAsync(clientId, ct);
        var assets = await (from a in db.Set<BrandAsset>().AsNoTracking()
                            join f in db.Set<DeliveryFile>() on a.FileId equals f.Id
                            where a.ClientAccountId == clientId
                            orderby a.Kind, a.CreatedAt
                            select new { a, f }).ToListAsync(ct);
        return new BrandKitDto(clientId, kit.Colors, kit.Fonts, kit.ToneOfVoice, kit.Personas, kit.Competitors, kit.Dos, kit.Donts,
            kit.KeyMessages,
            assets.Select(x => new BrandAssetDto(x.a.Id, x.a.Kind, x.a.Label, x.f.Id, x.f.OriginalFileName, x.f.ContentType, x.f.SizeBytes,
                $"/api/v1/agency/files/{x.f.Id}", $"/api/v1/client/orgs/{clientId}/files/{x.f.Id}", x.a.CreatedAt)).ToList(),
            kit.UpdatedAt, kit.ConcurrencyStamp);
    }

    public async Task<BrandKitDto> UpdateBrandKitAsync(Guid clientId, UpdateBrandKitRequest r, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var kit = await EnsureKitAsync(clientId, ct, tracked: true);
        DeliveryRules.EnsureStamp(kit, r.ConcurrencyStamp, db);
        if (r.Colors.Count > 24) throw DeliveryRules.Invalid("brand.too_many_colors", "colors", "At most 24 colours.");
        var colors = new List<BrandColor>();
        foreach (var c in r.Colors)
        {
            var hex = (c.Hex ?? string.Empty).Trim().ToUpperInvariant();
            if (!HexColor().IsMatch(hex)) throw DeliveryRules.Invalid("brand.invalid_color", "colors", "Colours must be hex values like #1F2659.");
            var name = (c.Name ?? string.Empty).Trim();
            if (name.Length is 0 or > 60) throw DeliveryRules.Invalid("brand.invalid_color_name", "colors", "Give each colour a name (max 60 characters).");
            colors.Add(new BrandColor(name, hex));
        }
        if (r.Personas.Count > 12) throw DeliveryRules.Invalid("brand.too_many_personas", "personas", "At most 12 personas.");
        var personas = r.Personas.Select(p => new BrandPersona((p.Name ?? "").Trim(), (p.Description ?? "").Trim()))
            .Where(p => p.Name.Length > 0).ToList();
        if (personas.Any(p => p.Name.Length > 100 || p.Description.Length > 2000))
            throw DeliveryRules.Invalid("brand.persona_too_long", "personas", "Persona names max 100 and descriptions max 2000 characters.");

        var before = new { kit.Colors, kit.Fonts, kit.ToneOfVoice };
        kit.Colors = colors;
        kit.Fonts = DeliveryRules.CleanList(r.Fonts, 12, 100, "fonts");
        kit.ToneOfVoice = string.IsNullOrWhiteSpace(r.ToneOfVoice) ? null : r.ToneOfVoice.Trim();
        kit.Personas = personas;
        kit.Competitors = DeliveryRules.CleanList(r.Competitors, 30, 200, "competitors");
        kit.Dos = DeliveryRules.CleanList(r.Dos, 30, 300, "dos");
        kit.Donts = DeliveryRules.CleanList(r.Donts, 30, 300, "donts");
        kit.KeyMessages = DeliveryRules.CleanList(r.KeyMessages, 20, 500, "keyMessages");
        kit.UpdatedByUserId = currentUser.Id;
        audit.Record("client.brand_kit_updated", nameof(BrandKit), kit.Id, before, new { kit.Colors, kit.Fonts, kit.ToneOfVoice });
        await db.SaveChangesAsync(ct);
        return await BrandKitAsync(clientId, ct);
    }

    /// <summary>Adds a brand asset. Staff (deliverables.submit) or a client Owner.</summary>
    public async Task<BrandKitDto> AddAssetAsync(Guid clientId, BrandAssetForm form, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, scope.IsStaff ? ClientMemberRole.Viewer : ClientMemberRole.Owner, ct);
        if (form.Kind is var kind && !Enum.IsDefined(kind)) throw DeliveryRules.Invalid("brand.invalid_kind", "kind", "Choose an asset type.");
        var file = await files.SaveAsync(clientId, form.File, ct);
        var asset = new BrandAsset
        {
            ClientAccountId = clientId, FileId = file.Id, Kind = form.Kind,
            Label = string.IsNullOrWhiteSpace(form.Label) ? file.OriginalFileName : form.Label.Trim(),
            UploadedByUserId = currentUser.Id, CreatedAt = Now,
        };
        db.Set<BrandAsset>().Add(asset);
        if (!scope.IsStaff)
        {
            // A client uploading brand assets completes their onboarding step.
            await db.Set<ClientOnboardingItem>()
                .Where(i => i.ClientAccountId == clientId && i.Key == "brand-assets" && i.Status == OnboardingItemStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, OnboardingItemStatus.Done).SetProperty(i => i.CompletedAt, Now)
                    .SetProperty(i => i.CompletedByUserId, currentUser.Id), ct);
        }
        audit.Record("client.brand_asset_added", nameof(BrandAsset), asset.Id, after: new { asset.Kind, asset.Label, file.ContentType, file.SizeBytes });
        await files.CommitAsync(file, ct);
        return await BrandKitAsync(clientId, ct);
    }

    /// <summary>
    /// Removes a brand asset (staff with clients.manage). Concurrency-safe: removals of the client's files are serialized
    /// and the row is locked, so a second (or concurrent) removal is a 404. The file itself is deleted (row and bytes)
    /// once nothing else refers to it; a file still used elsewhere (a message, a task) is kept.
    /// </summary>
    public async Task<BrandKitDto> RemoveAssetAsync(Guid clientId, Guid assetId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        string? purged;
        await using (await files.LockClientFilesAsync(clientId, ct))
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            if (!await dialect.LockRowAsync(db, "brand_assets", assetId, ct)) throw DomainException.NotFound("BrandAsset");
            var asset = await db.Set<BrandAsset>().FirstOrDefaultAsync(a => a.Id == assetId && a.ClientAccountId == clientId, ct)
                        ?? throw DomainException.NotFound("BrandAsset");
            db.Remove(asset);
            await db.SaveChangesAsync(ct);
            purged = await files.StageRemovalIfUnreferencedAsync(asset.FileId, ct);
            audit.Record("client.brand_asset_removed", nameof(BrandAsset), asset.Id,
                before: new { asset.ClientAccountId, asset.Kind, asset.Label, asset.FileId }, after: new { FileDeleted = purged is not null });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        files.DeleteStoredBytes(purged);
        return await BrandKitAsync(clientId, ct);
    }

    private async Task<BrandKit> EnsureKitAsync(Guid clientId, CancellationToken ct, bool tracked = false)
    {
        var q = tracked ? db.Set<BrandKit>() : db.Set<BrandKit>().AsNoTracking();
        var kit = await q.FirstOrDefaultAsync(k => k.ClientAccountId == clientId, ct);
        if (kit is not null) return kit;
        kit = new BrandKit { ClientAccountId = clientId };
        db.Set<BrandKit>().Add(kit);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.Entry(kit).State = EntityState.Detached;
            return await q.FirstAsync(k => k.ClientAccountId == clientId, ct);
        }
        return kit;
    }

    [GeneratedRegex("^#[0-9A-F]{6}$")]
    private static partial Regex HexColor();

    // ------------------------------------------------------------------ feedback

    public static string Quarter(DateTime utc) => $"{utc.Year}-Q{(utc.Month - 1) / 3 + 1}";

    public async Task<NpsStatusDto> NpsStatusAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var period = Quarter(Now);
        var key = $"nps:{period}:{clientId}:{currentUser.Id}";
        var mine = await db.Set<ClientFeedback>().AsNoTracking().Where(f => f.DedupeKey == key).Select(f => (int?)f.Score).FirstOrDefaultAsync(ct);
        return new NpsStatusDto(period, mine is null, mine);
    }

    public async Task<NpsStatusDto> SubmitNpsAsync(Guid clientId, SubmitNpsRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var period = Quarter(Now);
        if (await db.Set<ClientFeedback>().AnyAsync(f => f.DedupeKey == $"nps:{period}:{clientId}:{currentUser.Id}", ct))
            throw DomainException.Conflict("feedback.already_submitted", "You've already answered this quarter's survey. Thank you!");
        db.Set<ClientFeedback>().Add(new ClientFeedback
        {
            ClientAccountId = clientId, UserId = currentUser.Id, Kind = ClientFeedbackKind.Nps, Score = request.Score!.Value,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(), Period = period,
            DedupeKey = $"nps:{period}:{clientId}:{currentUser.Id}", CreatedAt = Now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("feedback.already_submitted", "You've already answered this quarter's survey. Thank you!");
        }
        return await NpsStatusAsync(clientId, ct);
    }

    /// <summary>CSAT for an approved deliverable of the organization (once per user and deliverable).</summary>
    public async Task SubmitCsatAsync(Guid clientId, Guid deliverableId, SubmitCsatRequest request, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var deliverable = await db.Set<Deliverable>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == deliverableId && d.ClientAccountId == clientId, ct)
                          ?? throw DomainException.NotFound("Deliverable");
        if (deliverable.Status is not (DeliverableStatus.Approved or DeliverableStatus.Published))
            throw DomainException.Conflict("feedback.not_approved", "You can rate a deliverable once it is approved.");
        if (await db.Set<ClientFeedback>().AnyAsync(f => f.DedupeKey == $"csat:{deliverableId}:{currentUser.Id}", ct))
            throw DomainException.Conflict("feedback.already_submitted", "You've already rated this deliverable.");
        db.Set<ClientFeedback>().Add(new ClientFeedback
        {
            ClientAccountId = clientId, UserId = currentUser.Id, Kind = ClientFeedbackKind.Csat, Score = request.Score!.Value,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(), DeliverableId = deliverableId,
            DedupeKey = $"csat:{deliverableId}:{currentUser.Id}", CreatedAt = Now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("feedback.already_submitted", "You've already rated this deliverable.");
        }
    }

    public async Task<FeedbackSummaryDto> FeedbackSummaryAsync(Guid clientId, DateTime? since, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        var from = since ?? Now.AddDays(-365);
        var rows = await db.Set<ClientFeedback>().AsNoTracking().Where(f => f.ClientAccountId == clientId && f.CreatedAt >= from)
            .OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
        var csat = rows.Where(r => r.Kind == ClientFeedbackKind.Csat).ToList();
        var nps = rows.Where(r => r.Kind == ClientFeedbackKind.Nps).ToList();
        int promoters = nps.Count(n => n.Score >= 9), detractors = nps.Count(n => n.Score <= 6);
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);
        var deliverableIds = rows.Where(r => r.DeliverableId != null).Select(r => r.DeliverableId!.Value).ToList();
        var titles = await db.Set<Deliverable>().AsNoTracking().Where(d => deliverableIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        return new FeedbackSummaryDto(
            csat.Count == 0 ? null : Math.Round(csat.Average(c => c.Score), 2), csat.Count,
            nps.Count == 0 ? null : (int)Math.Round((promoters - detractors) * 100.0 / nps.Count), nps.Count, promoters,
            nps.Count - promoters - detractors, detractors,
            rows.Take(50).Select(r => new FeedbackItemDto(r.Id, r.Kind, r.Score, r.Comment, r.Period, r.DeliverableId,
                r.DeliverableId is { } d && titles.TryGetValue(d, out var t) ? t : null,
                users.TryGetValue(r.UserId, out var p) ? p : new PersonDto(r.UserId, "Former user", ""), r.CreatedAt)).ToList());
    }
}
