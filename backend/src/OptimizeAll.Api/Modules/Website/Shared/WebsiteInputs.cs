using System.ComponentModel.DataAnnotations;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Shared;

public sealed class SeoInput
{
    [MaxLength(70)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? OgImageUrl { get; set; }

    [MaxLength(500)]
    public string? CanonicalUrl { get; set; }

    public bool NoIndex { get; set; }
}

public sealed record SeoDto(string? Title, string? Description, string? OgImageUrl, string? CanonicalUrl, bool NoIndex)
{
    public static SeoDto From(SeoMeta? s) => new(s?.Title, s?.Description, s?.OgImageUrl, s?.CanonicalUrl, s?.NoIndex ?? false);
}

public sealed class FaqEntryInput
{
    [MaxLength(300)]
    public string? Question { get; set; }

    [MaxLength(3000)]
    public string? Answer { get; set; }
}

public sealed class ProcessStepInput
{
    [MaxLength(100)]
    public string? Title { get; set; }

    [MaxLength(600)]
    public string? Description { get; set; }
}

public sealed class ReorderInput
{
    /// <summary>Ids in the desired display order. Listed items get SortOrder 10, 20, 30…</summary>
    [Required, MinLength(1), MaxLength(500)]
    public List<Guid> Ids { get; set; } = new();
}

public sealed record ReorderResult(int Updated);

/// <summary>Campaign attribution captured by the web app on the visitor's first page view.</summary>
public sealed class UtmInput
{
    [MaxLength(150)]
    public string? Source { get; set; }

    [MaxLength(150)]
    public string? Medium { get; set; }

    [MaxLength(150)]
    public string? Campaign { get; set; }

    [MaxLength(150)]
    public string? Term { get; set; }

    [MaxLength(150)]
    public string? Content { get; set; }
}

/// <summary>Fields every public form carries: anti-spam (honeypot, form token), consent and attribution.</summary>
public abstract class PublicFormInput
{
    /// <summary>Honeypot: a visually hidden field humans leave empty. Anything here marks the submission as spam.</summary>
    [MaxLength(200)]
    public string? Nickname { get; set; }

    /// <summary>Token from <c>GET /public/forms/token</c>; proves the form was open long enough to be filled by a person.</summary>
    [Required, MaxLength(1000)]
    public string FormToken { get; set; } = string.Empty;

    /// <summary>Must be true: the visitor ticked the consent checkbox.</summary>
    public bool Consent { get; set; }

    /// <summary>Version of the consent text the visitor saw (from <c>GET /public/site</c> → consent).</summary>
    [Required, MaxLength(40)]
    public string ConsentVersion { get; set; } = string.Empty;

    public UtmInput? Utm { get; set; }

    [MaxLength(500)]
    public string? Referrer { get; set; }

    [MaxLength(500)]
    public string? LandingPath { get; set; }
}
