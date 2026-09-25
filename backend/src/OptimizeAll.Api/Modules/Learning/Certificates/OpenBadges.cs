using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>
/// Open Badges 2.0 hosted verification (IMS Global / 1EdTech, https://www.imsglobal.org/sites/default/files/Badges/OBv2p0Final/index.html):
/// the issuer <c>Profile</c>, one <c>BadgeClass</c> per course and one <c>Assertion</c> per certificate, each served as JSON-LD
/// at its own <c>id</c> URL (<c>verification.type = "hosted"</c>). The recipient is the holder's email, hashed with a
/// per-assertion salt (<c>sha256$hex(sha256(email + salt))</c>), so the public document never contains the address.
/// OB 2.0 was chosen over OB 3.0 because 3.0 credentials must carry a cryptographic proof (signing keys and key
/// management); hosted 2.0 assertions are verifiable by URL and accepted by badge backpacks and validators.
/// </summary>
public static class OpenBadges
{
    public const string Context = "https://w3id.org/openbadges/v2";

    public static string HashIdentity(string email, string salt) =>
        "sha256$" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant() + salt))).ToLowerInvariant();

    public static JsonObject Issuer(LearningIssuer issuer, LearningLinks links, string? email)
    {
        var profile = new JsonObject
        {
            ["@context"] = Context,
            ["type"] = "Issuer",
            ["id"] = links.OpenBadgesIssuer,
            ["name"] = issuer.Name,
            ["url"] = links.Absolute("/learn"),
            ["description"] = $"{issuer.Name}: free, practical courses in sales, marketing, SEO and AI with verifiable certificates.",
            ["image"] = links.Absolute("/og-image.png"),
        };
        if (!string.IsNullOrWhiteSpace(email)) profile["email"] = email;
        return profile;
    }

    public static JsonObject BadgeClass(CoursePack pack, LearningLinks links)
    {
        var badge = pack.Badge!;
        return new JsonObject
        {
            ["@context"] = Context,
            ["type"] = "BadgeClass",
            ["id"] = links.OpenBadgeClass(pack.Slug),
            ["name"] = badge.Name,
            ["description"] = badge.Description,
            ["image"] = links.BadgeImage(pack.Slug),
            ["criteria"] = new JsonObject
            {
                ["id"] = links.Course(pack.Slug),
                ["narrative"] = badge.Criteria,
            },
            ["issuer"] = links.OpenBadgesIssuer,
            ["tags"] = new JsonArray((pack.Skills ?? new()).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
        };
    }

    public static JsonObject Assertion(Certificate c, string recipientEmail, LearningLinks links) => new()
    {
        ["@context"] = Context,
        ["type"] = "Assertion",
        ["id"] = links.OpenBadgeAssertion(c.Id),
        ["recipient"] = new JsonObject
        {
            ["type"] = "email",
            ["hashed"] = true,
            ["salt"] = c.RecipientSalt,
            ["identity"] = HashIdentity(recipientEmail, c.RecipientSalt),
        },
        ["badge"] = links.OpenBadgeClass(c.CourseSlug),
        ["verification"] = new JsonObject { ["type"] = "hosted" },
        ["issuedOn"] = c.IssuedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        ["image"] = links.BadgeImage(c.CourseSlug),
        ["evidence"] = new JsonArray(new JsonObject
        {
            ["id"] = links.Verify(c.Id),
            ["narrative"] = c.Score is { } score
                ? $"Completed every lesson of \"{c.CourseTitle}\" and scored {score}% on the final assessment."
                : $"Completed \"{c.CourseTitle}\" (credential issued by staff).",
        }),
    };

    /// <summary>The revoked-assertion stub the spec asks for (served with 410 Gone).</summary>
    public static JsonObject Revoked(Certificate c, LearningLinks links) => new()
    {
        ["@context"] = Context,
        ["type"] = "Assertion",
        ["id"] = links.OpenBadgeAssertion(c.Id),
        ["revoked"] = true,
        ["revocationReason"] = "This credential has been revoked by the issuer.",
    };
}
