using System.Security.Cryptography;
using System.Text;

namespace OptimizeAll.Api.Common.Hosting;

public sealed class TestAccountOptions
{
    public const string Section = "TestAccounts";

    /// <summary>Domain of generated test-user emails (<c>test+&lt;slug&gt;@&lt;domain&gt;</c>). Use a domain you control or a reserved one.</summary>
    public string EmailDomain { get; set; } = "test.optimizeall.app";
}

/// <summary>Pure rules for test accounts and the non-production quick sign-in (unit-tested).</summary>
public static class TestAccounts
{
    /// <summary>Domain of the seeded demo accounts (see Seed/DemoAccounts and docs/DEMO.md), including client subdomains.</summary>
    public const string DemoDomain = "demo.optimizeall.app";

    private const string SlugAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>
    /// Environments where the quick sign-in may be switched on. An allow-list (not "anything but Production"), so a
    /// misspelt or custom production environment name ("Prod", "production-eu", "Production " …) fails closed.
    /// </summary>
    public static readonly IReadOnlySet<string> TestLoginEnvironments =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Development", "Staging", "Testing" };

    /// <summary>
    /// The quick "log in as test user" is available only when <c>DevTools:TestLoginEnabled</c> is on AND the host runs in
    /// one of <see cref="TestLoginEnvironments"/> (never Production). Both are required, so a copied configuration can
    /// never enable it in production. The environment name comes from the process environment
    /// (ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT), never from a request.
    /// </summary>
    public static bool TestLoginAllowed(bool configEnabled, string? environmentName) =>
        configEnabled && environmentName is not null && TestLoginEnvironments.Contains(environmentName);

    /// <summary>
    /// A seeded demo account: <c>x@demo.optimizeall.app</c> or <c>x@&lt;client&gt;.demo.optimizeall.app</c>. The domain
    /// must be plain ASCII (letters, digits, dots, dashes): look-alike Unicode characters that case-fold to ASCII
    /// (e.g. the Kelvin sign or dotted capital I) never match.
    /// </summary>
    public static bool IsDemoEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0) return false;
        var raw = email[(at + 1)..].Trim();
        if (raw.Length == 0 || !raw.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) return false;
        var domain = raw.ToLowerInvariant();
        if (domain == DemoDomain) return true;
        if (!domain.EndsWith("." + DemoDomain, StringComparison.Ordinal)) return false;
        // One or more non-empty labels before the demo domain ("..demo.optimizeall.app" is not a subdomain).
        var prefix = domain[..^(DemoDomain.Length + 1)];
        return prefix.Split('.').All(label => label.Length > 0);
    }

    /// <summary>Lower-case ASCII slug of letters/digits and single dashes, at most <paramref name="max"/> characters.</summary>
    public static string Slugify(string text, int max = 24)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-' && (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')) sb.Append('-');
            if (sb.Length >= max) break;
        }
        return sb.ToString().Trim('-');
    }

    /// <summary><c>test+&lt;slug&gt;-&lt;random&gt;@domain</c>; the random suffix keeps emails unique.</summary>
    public static string GenerateEmail(string label, string domain)
    {
        var slug = Slugify(label);
        var suffix = RandomString(SlugAlphabet, 6);
        var local = string.IsNullOrEmpty(slug) ? $"test+{suffix}" : $"test+{slug}-{suffix}";
        return $"{local}@{domain.Trim().TrimStart('@').ToLowerInvariant()}";
    }

    /// <summary>A random 20-character password (shown once to the admin who creates the test user).</summary>
    public static string GeneratePassword() => RandomString(PasswordAlphabet, 16) + "-" + RandomString(SlugAlphabet, 3);

    private static string RandomString(string alphabet, int length) =>
        new(Enumerable.Range(0, length).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
}
