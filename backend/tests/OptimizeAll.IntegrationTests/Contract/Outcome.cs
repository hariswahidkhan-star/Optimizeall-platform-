using System.Text.Json;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>A response read once: status, media type, body and (for JSON) the parsed document and problem <c>code</c>.</summary>
public sealed class Outcome
{
    public required int Status { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public JsonElement? Json { get; init; }

    public string? Code => Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
        ? c.GetString()
        : null;

    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The authentication challenge (token missing or rejected), as opposed to a business 401 with its own code.</summary>
    public bool IsChallenge => Status == 401 && (Body.Length == 0 || Code == "http_401");

    /// <summary>A model-validation / binding failure (400 with an <c>errors</c> map), as opposed to a business 400.</summary>
    public bool IsValidationFailure => Status == 400 && Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("errors", out _);

    /// <summary>A 403 produced by the permission checks (authorization middleware or [RequireAnyPermission]), not by a business rule.</summary>
    public bool IsPermissionDenial => Status == 403 && (Body.Length == 0 || Code is "auth.forbidden");

    public static async Task<Outcome> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            JsonElement? json = null;
            if (media.Contains("json", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
            {
                try { json = JsonSerializer.Deserialize<JsonElement>(body); } catch (JsonException) { }
            }
            return new Outcome { Status = (int)response.StatusCode, MediaType = media, Body = body, Json = json };
        }
    }

    public string Short => $"{Status} {(Code ?? (Body.Length > 160 ? Body[..160] + "…" : Body)).ReplaceLineEndings(" ")}";

    private static readonly string[] StackTraceMarkers =
    {
        "   at ", "Exception:", "System.InvalidOperationException", "System.NullReferenceException", "Microsoft.EntityFrameworkCore",
        "MySqlConnector", "SqliteException", "OptimizeAll.Api.",
    };

    /// <summary>
    /// The contract every response keeps whatever the input: never a 5xx (except a documented 503 "not configured" problem),
    /// never internals in the body, and every 4xx is an RFC 7807 problem with <c>code</c> and <c>traceId</c>.
    /// Returns the violation, or null.
    /// </summary>
    public string? ContractViolation()
    {
        if (Status >= 500 && !(Status == 503 && Code is not null)) return $"[5xx] {Short}";
        if (StackTraceMarkers.Any(m => Body.Contains(m, StringComparison.Ordinal)) && !MediaType.Contains("csv") && !MediaType.StartsWith("text/html"))
        {
            // Business content can legitimately mention these words only in real payloads; errors never may.
            if (Status >= 400)
            {
                var marker = StackTraceMarkers.First(m => Body.Contains(m, StringComparison.Ordinal));
                var at = Math.Max(0, Body.IndexOf(marker, StringComparison.Ordinal) - 60);
                return $"[leak] {Status} body exposes internals: …{Body.Substring(at, Math.Min(160, Body.Length - at)).ReplaceLineEndings(" ")}…";
            }
        }
        if (Status is >= 400 and < 500)
        {
            if (!MediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase))
                return $"[problem] {Status} is not problem+json (media '{MediaType}', body '{Short}')";
            if (Json is not { ValueKind: JsonValueKind.Object } j || Code is null || !j.TryGetProperty("traceId", out var t) || t.ValueKind != JsonValueKind.String)
                return $"[problem] {Status} problem lacks code/traceId: {Short}";
        }
        return null;
    }
}
