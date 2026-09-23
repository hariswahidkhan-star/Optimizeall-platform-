namespace OptimizeAll.Domain.Marketing;

/// <summary>Stable fraud signal codes stored (comma-separated) in <see cref="Referral.FraudSignals"/>.</summary>
public static class ReferralFraudSignals
{
    /// <summary>Another referral by the same referrer registered from the same (hashed) IP in the last 30 days.</summary>
    public const string SharedIp = "shared_ip";

    /// <summary>Another referral by the same referrer (or the referrer's own registration) used the same device.</summary>
    public const string SharedDevice = "shared_device";

    /// <summary>Referred email is an alias of the referrer's email ("+tag", Gmail dots).</summary>
    public const string EmailAlias = "email_alias";

    /// <summary>More than <see cref="ReferralFraudRules.VelocityLimit"/> referrals by the referrer within 24 hours.</summary>
    public const string Velocity = "velocity";

    /// <summary>Referred email uses a known disposable-mail domain.</summary>
    public const string DisposableEmail = "disposable_email";
}

/// <summary>An earlier referral by the same referrer, as seen by the fraud rules.</summary>
public sealed record PriorReferral(string? IpHash, string? DeviceHash, DateTime CreatedAt);

/// <summary>Inputs for evaluating one new referral.</summary>
public sealed record ReferralFraudContext(
    string ReferrerEmail,
    string ReferredEmail,
    string? IpHash,
    string? DeviceHash,
    IReadOnlyCollection<PriorReferral> PriorReferralsByReferrer,
    DateTime NowUtc,
    string? ReferrerOwnIpHash = null,
    string? ReferrerOwnDeviceHash = null);

/// <summary>
/// Pure referral fraud heuristics. Signals do not block a referral; they force manual approval of the reward
/// and are shown to the marketing team, who can reject the referral.
/// </summary>
public static class ReferralFraudRules
{
    public static readonly TimeSpan SharedIpWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan VelocityWindow = TimeSpan.FromHours(24);

    /// <summary>A referrer with more than this many referrals in 24 hours (including the new one) is flagged.</summary>
    public const int VelocityLimit = 10;

    /// <summary>Small built-in list of well-known disposable email domains.</summary>
    public static readonly IReadOnlySet<string> DisposableDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "guerrillamail.net", "sharklasers.com", "10minutemail.com",
        "temp-mail.org", "tempmail.com", "yopmail.com", "trashmail.com", "getnada.com", "dispostable.com",
        "maildrop.cc", "throwawaymail.com", "fakeinbox.com", "mailnesia.com",
    };

    public static IReadOnlyList<string> Evaluate(ReferralFraudContext context)
    {
        var signals = new List<string>();

        if (context.IpHash is not null &&
            (context.PriorReferralsByReferrer.Any(p =>
                 p.IpHash == context.IpHash && p.CreatedAt >= context.NowUtc - SharedIpWindow) ||
             context.ReferrerOwnIpHash == context.IpHash))
            signals.Add(ReferralFraudSignals.SharedIp);

        if (context.DeviceHash is not null &&
            (context.PriorReferralsByReferrer.Any(p => p.DeviceHash == context.DeviceHash) ||
             context.ReferrerOwnDeviceHash == context.DeviceHash))
            signals.Add(ReferralFraudSignals.SharedDevice);

        if (IsEmailAlias(context.ReferrerEmail, context.ReferredEmail))
            signals.Add(ReferralFraudSignals.EmailAlias);

        var recent = context.PriorReferralsByReferrer.Count(p => p.CreatedAt >= context.NowUtc - VelocityWindow) + 1;
        if (recent > VelocityLimit)
            signals.Add(ReferralFraudSignals.Velocity);

        if (IsDisposable(context.ReferredEmail))
            signals.Add(ReferralFraudSignals.DisposableEmail);

        return signals;
    }

    /// <summary>
    /// Canonical mailbox: lower-case, "+tag" removed from the local part, and for gmail.com/googlemail.com dots
    /// removed and the domain unified to gmail.com.
    /// </summary>
    public static string CanonicalMailbox(string email)
    {
        var trimmed = email.Trim().ToLowerInvariant();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1) return trimmed;

        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        if (domain is "gmail.com" or "googlemail.com")
        {
            local = local.Replace(".", string.Empty);
            domain = "gmail.com";
        }
        return local + "@" + domain;
    }

    public static bool IsEmailAlias(string referrerEmail, string referredEmail) =>
        !string.IsNullOrWhiteSpace(referrerEmail) && !string.IsNullOrWhiteSpace(referredEmail) &&
        CanonicalMailbox(referrerEmail) == CanonicalMailbox(referredEmail);

    public static bool IsDisposable(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0) return false;
        var domain = email[(at + 1)..].Trim().TrimEnd('.');
        // Match the domain itself and any subdomain of a listed domain.
        return DisposableDomains.Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                                         domain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    public static string? Join(IReadOnlyCollection<string> signals) => signals.Count == 0 ? null : string.Join(',', signals);

    public static IReadOnlyList<string> Split(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? Array.Empty<string>()
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
