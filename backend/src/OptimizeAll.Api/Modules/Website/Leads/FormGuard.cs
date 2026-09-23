using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>
/// Versioned consent wording shown next to public forms. When the wording changes, bump the version: submissions store
/// the version the visitor agreed to, and a stale version is rejected so the visitor sees (and agrees to) the new text.
/// </summary>
public static class ConsentTexts
{
    public const string FormVersion = "forms-2026-09";
    public const string FormText =
        "I agree that Optimize All may use the details I've submitted to respond to my request, as described in the Privacy Policy.";

    public const string NewsletterVersion = "newsletter-2026-09";
    public const string NewsletterText =
        "Send me Optimize All's marketing newsletter (about twice a month). I can unsubscribe at any time with the link in every email.";

    public const string CareersVersion = "careers-2026-09";
    public const string CareersText =
        "I agree that Optimize All may store my application and CV to assess me for this and similar roles for up to 12 months, as described in the Privacy Policy.";

    public static readonly ConsentTextsDto Dto = new(FormVersion, FormText, NewsletterVersion, NewsletterText, CareersVersion, CareersText);
}

public sealed record FormOption(string Value, string Label);

public sealed record FormTokenDto(string Token, int MinFillSeconds, IReadOnlyList<FormOption> BudgetRanges, IReadOnlyList<FormOption> Timelines);

public enum FormCheck
{
    Accepted,
    /// <summary>Honeypot filled: answer as if accepted, store nothing.</summary>
    Spam,
}

/// <summary>
/// Anti-spam checks for every public form: a hidden honeypot field, a signed form token that proves the form was open
/// for a minimum time (and expires after a day), and the consent checkbox with the current wording version. Combined
/// with the <c>public</c> rate-limit policy (120 requests/minute per IP) on the endpoints.
/// </summary>
public sealed class FormGuard(IDataProtectionProvider protection, TimeProvider clock, IConfiguration configuration)
{
    private readonly IDataProtector _protector = protection.CreateProtector("OptimizeAll.Website.FormToken.v1");

    public static readonly FormOption[] BudgetRanges =
    {
        new("under-1k", "Under $1,000 / month"),
        new("1k-3k", "$1,000 – $3,000 / month"),
        new("3k-10k", "$3,000 – $10,000 / month"),
        new("10k-25k", "$10,000 – $25,000 / month"),
        new("25k-plus", "$25,000+ / month"),
        new("not-sure", "Not sure yet"),
    };

    public static readonly FormOption[] Timelines =
    {
        new("asap", "As soon as possible"),
        new("1-3-months", "In 1–3 months"),
        new("3-6-months", "In 3–6 months"),
        new("exploring", "Just exploring"),
    };

    public int MinFillSeconds => Math.Max(0, configuration.GetValue("Website:MinFormFillSeconds", 3));

    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public FormTokenDto Issue()
    {
        var issued = clock.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture);
        return new FormTokenDto(_protector.Protect($"{issued}:{Guid.NewGuid():N}"), MinFillSeconds, BudgetRanges, Timelines);
    }

    /// <summary>Runs the checks. Throws 400 for an expired/invalid token, a too-fast submission or missing consent.</summary>
    public FormCheck Check(PublicFormInput input, string consentVersion)
    {
        if (!string.IsNullOrWhiteSpace(input.Nickname)) return FormCheck.Spam;

        DateTime issued;
        try
        {
            var raw = _protector.Unprotect(input.FormToken);
            var ticks = long.Parse(raw.Split(':')[0], CultureInfo.InvariantCulture);
            issued = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or OverflowException or ArgumentException)
        {
            throw Expired();
        }
        var elapsed = clock.GetUtcNow().UtcDateTime - issued;
        if (elapsed > TokenLifetime || elapsed < TimeSpan.FromSeconds(-5)) throw Expired();
        if (elapsed < TimeSpan.FromSeconds(MinFillSeconds))
            throw new DomainException("website.form_too_fast", "That was quick! Please check your answers and send the form again.");

        var e = new FieldErrors();
        if (!input.Consent) e.Add("consent", "Please tick the box to agree before sending.");
        else if (input.ConsentVersion != consentVersion) e.Add("consent", "The consent wording has changed. Reload the page, review it and try again.");
        e.ThrowIfAny("website.consent_required", "Please agree to how we'll use your details.");
        return FormCheck.Accepted;
    }

    private static DomainException Expired() =>
        new("website.form_expired", "This form has expired. Reload the page and try again.");
}
