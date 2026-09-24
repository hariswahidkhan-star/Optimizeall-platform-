using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>
/// Makes public form tokens single-use: a successful submission stores the hash of its token in
/// <see cref="UsedFormToken"/> (unique) in the same SaveChanges as the submission, so replaying the request (or sending
/// it twice at once) cannot create a second inquiry, booking or application with one token. Spam (honeypot) answers
/// and failed validations do not spend the token.
/// </summary>
public sealed class FormTokenLedger(AppDbContext db, FormGuard guard, TimeProvider clock)
{
    public static DomainException AlreadyUsed() => DomainException.Conflict("website.form_already_submitted",
        "This form has already been sent. Reload the page if you'd like to send another message.");

    /// <summary>Fails fast when the token was already spent (the unique index is the real guard).</summary>
    public async Task EnsureUnusedAsync(PublicFormInput input, CancellationToken ct)
    {
        var (hash, _) = guard.Identify(input.FormToken);
        if (await db.Set<UsedFormToken>().AnyAsync(t => t.TokenHash == hash, ct)) throw AlreadyUsed();
    }

    /// <summary>Stages the "used" row; the caller saves it together with the submission.</summary>
    public UsedFormToken Spend(PublicFormInput input)
    {
        var (hash, expiresAt) = guard.Identify(input.FormToken);
        var row = new UsedFormToken { TokenHash = hash, UsedAt = clock.GetUtcNow().UtcDateTime, ExpiresAt = expiresAt };
        db.Set<UsedFormToken>().Add(row);
        return row;
    }

    /// <summary>
    /// After a unique violation while saving a submission: true when the violation was this token having been spent by
    /// another (concurrent) request, i.e. a replay.
    /// </summary>
    public Task<bool> SpentElsewhereAsync(UsedFormToken spent, CancellationToken ct)
    {
        var (hash, id) = (spent.TokenHash, spent.Id);
        return db.Set<UsedFormToken>().AsNoTracking().AnyAsync(t => t.TokenHash == hash && t.Id != id, ct);
    }

    /// <summary>Saves the submission together with the spent token; a concurrent replay of the token gets a 409.</summary>
    public async Task SaveAsync(UsedFormToken spent, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            if (await SpentElsewhereAsync(spent, ct)) throw AlreadyUsed();
            throw;
        }
    }
}

/// <summary>Deletes spent-token rows whose token has expired (it would be rejected as expired anyway).</summary>
public sealed class UsedFormTokenCleanupJob(AppDbContext db, TimeProvider clock) : IJob
{
    public string Name => nameof(UsedFormTokenCleanupJob);

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        // A small grace period covers clock skew between instances; FormGuard also accepts tokens up to 5 s "early".
        var cutoff = clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
        var deleted = await db.Set<UsedFormToken>().Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
        return $"deleted {deleted} expired used form token(s)";
    }
}
