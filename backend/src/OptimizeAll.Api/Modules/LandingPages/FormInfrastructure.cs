using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages;

/// <summary>
/// Private storage for form uploads (PDF/PNG/JPEG/WebP) under <c>{Storage:RootPath}/form-uploads</c>. Keys are
/// server-generated (<c>yyyy/MM/{guid}.ext</c>) and validated before any file-system access, so client file names never
/// reach a path. Files are only served to staff with forms.manage through the submissions API.
/// </summary>
public sealed partial class FormFileStore
{
    private readonly string _root;

    public FormFileStore(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Storage:RootPath"] is { Length: > 0 } p ? p : "storage/files";
        var baseRoot = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _root = Path.GetFullPath(Path.Combine(baseRoot, "form-uploads"));
    }

    public string NewKey(DateTime now, string extension)
    {
        if (!ExtensionRegex().IsMatch(extension)) throw new ArgumentException("Unsupported extension.", nameof(extension));
        return $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
    }

    public async Task WriteAsync(string key, byte[] content, CancellationToken ct)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await stream.WriteAsync(content, ct);
    }

    public Stream? OpenRead(string key)
    {
        var path = Resolve(key);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    public void Delete(string key)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
    }

    private string Resolve(string key)
    {
        if (!KeyRegex().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return full;
    }

    [GeneratedRegex(@"^\.(pdf|png|jpg|webp)$")]
    private static partial Regex ExtensionRegex();

    [GeneratedRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}\.(pdf|png|jpg|webp)$")]
    private static partial Regex KeyRegex();
}

/// <summary>
/// Server-side CAPTCHA verification (hCaptcha or Cloudflare Turnstile) with credentials from the vault (provider
/// "hcaptcha"/"turnstile": setting <c>siteKey</c>, secret <c>secretKey</c>). Not configured → the check is skipped.
/// </summary>
public sealed class CaptchaVerifier(HttpClient http, ICredentialVault vault, IConfiguration configuration)
{
    public enum Result
    {
        Passed,
        Failed,
        Skipped,
    }

    public static string ProviderKey(CaptchaProvider provider) => provider == CaptchaProvider.HCaptcha ? "hcaptcha" : "turnstile";

    /// <summary>The public site key when the provider is configured for the client (else null → no widget).</summary>
    public async Task<string?> SiteKeyAsync(CaptchaProvider provider, Guid clientId, CancellationToken ct)
    {
        if (provider == CaptchaProvider.None) return null;
        var credentials = await vault.GetAsync(ProviderKey(provider), clientId, ct);
        return credentials is not null && credentials.Settings.TryGetValue("siteKey", out var key) && !string.IsNullOrWhiteSpace(key) ? key : null;
    }

    public async Task<Result> VerifyAsync(CaptchaProvider provider, Guid clientId, string? token, string? remoteIp, CancellationToken ct)
    {
        if (provider == CaptchaProvider.None) return Result.Skipped;
        var credentials = await vault.GetAsync(ProviderKey(provider), clientId, ct);
        if (credentials is null || !credentials.Secrets.TryGetValue("secretKey", out var secret) || string.IsNullOrWhiteSpace(secret))
            return Result.Skipped;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return Result.Failed;

        var url = provider == CaptchaProvider.HCaptcha
            ? configuration["Forms:Captcha:HCaptchaVerifyUrl"] ?? "https://api.hcaptcha.com/siteverify"
            : configuration["Forms:Captcha:TurnstileVerifyUrl"] ?? "https://challenges.cloudflare.com/turnstile/v0/siteverify";
        var fields = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) fields["remoteip"] = remoteIp;
        try
        {
            using var response = await http.PostAsync(url, new FormUrlEncodedContent(fields), ct);
            if (!response.IsSuccessStatusCode) return Result.Failed;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True ? Result.Passed : Result.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Fail closed: a configured CAPTCHA that cannot be verified rejects the submission.
            return Result.Failed;
        }
    }
}

/// <summary>
/// Signed "form rendered at" tokens for the minimum-fill-time check. The token binds the form id and the server time the
/// form was served (Data Protection), so the client cannot fake how long the visitor spent on the form.
/// </summary>
public sealed class FormRenderTokens(IDataProtectionProvider protection, TimeProvider clock)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private readonly IDataProtector _protector = protection.CreateProtector("OptimizeAll.Forms.RenderToken.v1");

    public string Issue(Guid formId) => _protector.Protect($"{formId:N}|{clock.GetUtcNow().UtcTicks}");

    /// <summary>Seconds since the form was rendered, or null when the token is missing, forged, for another form or expired.</summary>
    public double? ElapsedSeconds(string? token, Guid formId)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1000) return null;
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            if (parts.Length != 2 || parts[0] != formId.ToString("N") || !long.TryParse(parts[1], out var ticks)) return null;
            var elapsed = clock.GetUtcNow().UtcDateTime - new DateTime(ticks, DateTimeKind.Utc);
            return elapsed < TimeSpan.Zero || elapsed > MaxAge ? null : elapsed.TotalSeconds;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Delivers autoresponder emails from the form outbox with retry/backoff (5 attempts).</summary>
public sealed class FormEmailDispatchJob(AppDbContext db, IEmailSender email, TimeProvider clock) : IJob
{
    public const int MaxAttempts = 5;
    public string Name => "forms.email-dispatch";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Set<FormEmailOutbox>().AsNoTracking()
            .Where(o => o.Status == OutboxEmailStatus.Pending && o.NextAttemptAt <= now).OrderBy(o => o.NextAttemptAt).Take(50).ToListAsync(ct);
        int sent = 0, failed = 0;
        foreach (var row in due)
        {
            // Claim: push the next attempt out so a parallel run skips this row.
            var lease = now.AddMinutes(10);
            var claimed = await db.Set<FormEmailOutbox>()
                .Where(o => o.Id == row.Id && o.Status == OutboxEmailStatus.Pending && o.NextAttemptAt == row.NextAttemptAt)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.NextAttemptAt, lease).SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
            if (claimed == 0) continue;

            var result = await email.SendAsync(new EmailMessage(row.ToAddress, row.ToName ?? row.ToAddress, row.Subject, row.Body), ct);
            var attempts = row.Attempts + 1;
            if (result.Success)
            {
                sent++;
                await db.Set<FormEmailOutbox>().Where(o => o.Id == row.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OutboxEmailStatus.Sent).SetProperty(o => o.SentAt, clock.GetUtcNow().UtcDateTime)
                        .SetProperty(o => o.LastError, (string?)null), ct);
            }
            else
            {
                failed++;
                var error = result.Error is { Length: > 1000 } e ? e[..1000] : result.Error;
                var next = now.AddMinutes(Math.Pow(2, attempts) * 5);
                await db.Set<FormEmailOutbox>().Where(o => o.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, attempts >= MaxAttempts ? OutboxEmailStatus.Failed : OutboxEmailStatus.Pending)
                        .SetProperty(o => o.NextAttemptAt, next).SetProperty(o => o.LastError, error), ct);
            }
        }
        return $"{sent} sent, {failed} failed";
    }
}
