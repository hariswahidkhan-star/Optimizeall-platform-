using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.Api.Modules.SocialMedia;

public enum PublishOutcome
{
    Published,
    Failed,
    NotConfigured,
    NotSupported,
}

public sealed record PublishMedia(MediaKind Kind, string? PublicUrl, string? AltText);

/// <summary>Everything an adapter needs to publish one variant. The token is decrypted just for this call.</summary>
public sealed record PublishRequest(
    Guid VariantId, SocialNetwork Network, string Handle, string? ExternalProfileId, string? AccessToken, string Text, string? Title,
    string? Link, string? FirstComment, IReadOnlyList<PublishMedia> Media);

/// <summary>
/// Result of a publish call. Only <see cref="PublishOutcome.Published"/> with a provider post id may mark a variant
/// published; every other outcome carries a human-readable message.
/// </summary>
public sealed record PublishResult(PublishOutcome Outcome, PublishFailureKind FailureKind, string? ExternalPostId, string? Url, string? Message)
{
    public static PublishResult Ok(string externalId, string? url, string? note = null) =>
        new(PublishOutcome.Published, PublishFailureKind.None, externalId, url, note);

    public static PublishResult Fail(PublishFailureKind kind, string message) => new(PublishOutcome.Failed, kind, null, null, message);

    public static PublishResult NotConfigured(string message) =>
        new(PublishOutcome.NotConfigured, PublishFailureKind.NotConfigured, null, null, message);

    public static PublishResult NotSupported(string message) =>
        new(PublishOutcome.NotSupported, PublishFailureKind.NotSupported, null, null, message);
}

/// <summary>
/// Adapter that publishes to one network. The publishing service uses the last registered publisher per network, so a
/// deployment (or a test) can replace an adapter by registering another implementation.
/// </summary>
public interface ISocialPublisher
{
    SocialNetwork Network { get; }
    Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct);
}

/// <summary>Adapter for networks whose publishing API is not implemented: never pretends to publish.</summary>
public sealed class NotConfiguredPublisher(SocialNetwork network) : ISocialPublisher
{
    public SocialNetwork Network => network;

    public Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct) =>
        Task.FromResult(PublishResult.NotConfigured(
            $"{PostValidator.Label(network)} publishing is not configured in this installation (no API adapter). " +
            "Publish it on the network yourself, then use \"Mark as published\" with the live URL."));
}

/// <summary>Error classification of provider responses.</summary>
internal static class ProviderErrors
{
    public static PublishResult FromHttp(string provider, HttpStatusCode status, string body, bool tokenExpired)
    {
        var detail = Truncate(body, 600);
        if (tokenExpired || status == HttpStatusCode.Unauthorized)
            return PublishResult.Fail(PublishFailureKind.Authorization, $"{provider} rejected the access token (expired or revoked). Reconnect the profile. {detail}");
        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
            return PublishResult.Fail(PublishFailureKind.Transient, $"{provider} returned {(int)status}; will retry. {detail}");
        return PublishResult.Fail(PublishFailureKind.Rejected, $"{provider} rejected the post ({(int)status}): {detail}");
    }

    public static PublishResult FromException(string provider, Exception ex) =>
        PublishResult.Fail(PublishFailureKind.Transient, $"{provider} request failed: {ex.Message}");

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Thin Graph API client with Meta's error model (error.code 190 = invalid/expired token; 4/17/32/613 = rate limit).</summary>
public sealed class MetaGraphClient(IHttpClientFactory factory, IOptions<SocialMediaOptions> options)
{
    public const string HttpClientName = "meta-graph";

    public sealed record GraphResponse(bool Success, HttpStatusCode Status, JsonElement Body, string Raw, int? ErrorCode)
    {
        public bool TokenExpired => ErrorCode is 190 or 102 or 463 or 467;
        public bool RateLimited => ErrorCode is 4 or 17 or 32 or 613 or 80001 or 80004;
    }

    public string BaseUrl => options.Value.GraphApiBaseUrl.TrimEnd('/');

    public Task<GraphResponse> PostAsync(string path, IDictionary<string, string> form, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, form, token, ct);

    public Task<GraphResponse> GetAsync(string pathAndQuery, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, pathAndQuery, null, token, ct);

    private async Task<GraphResponse> SendAsync(HttpMethod method, string path, IDictionary<string, string>? form, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{BaseUrl}/{path.TrimStart('/')}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await factory.CreateClient(HttpClientName).SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonElement body;
        try
        {
            body = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            body = JsonDocument.Parse("{}").RootElement.Clone();
        }
        int? code = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var err) && err.TryGetProperty("code", out var c)
                    && c.TryGetInt32(out var n) ? n : null;
        return new GraphResponse(response.IsSuccessStatusCode && code is null, response.StatusCode, body, raw, code);
    }

    public static PublishResult Failure(string provider, GraphResponse r)
    {
        var message = r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)
            ? m.GetString() ?? r.Raw
            : r.Raw;
        if (r.RateLimited) return PublishResult.Fail(PublishFailureKind.Transient, $"{provider} rate limit reached; will retry. {message}");
        return ProviderErrors.FromHttp(provider, r.Status == HttpStatusCode.OK ? HttpStatusCode.BadRequest : r.Status, message, r.TokenExpired);
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Facebook Page publishing through the Graph API: text/link posts (<c>POST /{page-id}/feed</c>), one photo
/// (<c>/photos</c> with <c>url</c>), several photos (unpublished photos attached to a feed post), a video
/// (<c>/videos</c> with <c>file_url</c>) and an optional first comment (<c>POST /{post-id}/comments</c>).
/// </summary>
public sealed class FacebookPagePublisher(MetaGraphClient graph, ILogger<FacebookPagePublisher> logger) : ISocialPublisher
{
    public SocialNetwork Network => SocialNetwork.Facebook;

    public async Task<PublishResult> PublishAsync(PublishRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.AccessToken) || string.IsNullOrWhiteSpace(r.ExternalProfileId))
            return PublishResult.NotConfigured("The Facebook Page is not connected (no page token). Connect it, or publish manually and mark as published.");
        if (r.Media.Any(m => string.IsNullOrWhiteSpace(m.PublicUrl)))
            return PublishResult.NotSupported("Facebook fetches media by URL: mark the media as public or use an https URL.");

        try
        {
            var page = Uri.EscapeDataString(r.ExternalProfileId);
            MetaGraphClient.GraphResponse res;
            string? postId;
            var videos = r.Media.Where(m => m.Kind == MediaKind.Video).ToList();
            if (videos.Count > 0)
            {
                res = await graph.PostAsync($"{page}/videos", new Dictionary<string, string>
                {
                    ["file_url"] = videos[0].PublicUrl!,
                    ["description"] = r.Text,
                }, r.AccessToken, ct);
                if (!res.Success) return MetaGraphClient.Failure("Facebook", res);
                postId = MetaGraphClient.Str(res.Body, "id");
            }
            else if (r.Media.Count == 1)
            {
                res = await graph.PostAsync($"{page}/photos", new Dictionary<string, string>
                {
                    ["url"] = r.Media[0].PublicUrl!,
                    ["caption"] = r.Text,
                    ["published"] = "true",
                }, r.AccessToken, ct);
                if (!res.Success) return MetaGraphClient.Failure("Facebook", res);
                postId = MetaGraphClient.Str(res.Body, "post_id") ?? MetaGraphClient.Str(res.Body, "id");
            }
            else
            {
                var form = new Dictionary<string, string> { ["message"] = r.Text };
                if (!string.IsNullOrWhiteSpace(r.Link)) form["link"] = r.Link;
                for (var i = 0; i < r.Media.Count; i++)
                {
                    var photo = await graph.PostAsync($"{page}/photos", new Dictionary<string, string>
                    {
                        ["url"] = r.Media[i].PublicUrl!,
                        ["published"] = "false",
                    }, r.AccessToken, ct);
                    if (!photo.Success) return MetaGraphClient.Failure("Facebook", photo);
                    form[$"attached_media[{i}]"] = JsonSerializer.Serialize(new { media_fbid = MetaGraphClient.Str(photo.Body, "id") });
                }
                res = await graph.PostAsync($"{page}/feed", form, r.AccessToken, ct);
                if (!res.Success) return MetaGraphClient.Failure("Facebook", res);
                postId = MetaGraphClient.Str(res.Body, "id");
            }

            if (string.IsNullOrWhiteSpace(postId))
                return PublishResult.Fail(PublishFailureKind.Unknown, $"Facebook answered without a post id: {ProviderErrors.Truncate(res.Raw, 300)}");

            string? note = null;
            if (!string.IsNullOrWhiteSpace(r.FirstComment))
            {
                var comment = await graph.PostAsync($"{Uri.EscapeDataString(postId)}/comments",
                    new Dictionary<string, string> { ["message"] = r.FirstComment }, r.AccessToken, ct);
                if (!comment.Success) note = "Published, but the first comment failed: " + ProviderErrors.Truncate(comment.Raw, 200);
            }
            return PublishResult.Ok(postId, $"https://www.facebook.com/{postId}", note);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Facebook publish failed for variant {Variant}", r.VariantId);
            return ProviderErrors.FromException("Facebook", ex);
        }
    }
}

/// <summary>
/// Instagram content publishing (Graph API): create a media container (<c>POST /{ig-user-id}/media</c> with
/// <c>image_url</c>, or <c>media_type=REELS</c> + <c>video_url</c>, or a CAROUSEL of child containers), wait until video
/// containers are FINISHED, then <c>POST /{ig-user-id}/media_publish</c>. Media must be reachable by URL.
/// </summary>
public sealed class InstagramPublisher(MetaGraphClient graph, IOptions<SocialMediaOptions> options, ILogger<InstagramPublisher> logger)
    : ISocialPublisher
{
    public SocialNetwork Network => SocialNetwork.Instagram;

    public async Task<PublishResult> PublishAsync(PublishRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.AccessToken) || string.IsNullOrWhiteSpace(r.ExternalProfileId))
            return PublishResult.NotConfigured("The Instagram account is not connected. Connect it, or publish manually and mark as published.");
        if (r.Media.Count == 0)
            return PublishResult.Fail(PublishFailureKind.Rejected, "Instagram posts need at least one image or video.");
        if (r.Media.Any(m => string.IsNullOrWhiteSpace(m.PublicUrl)))
            return PublishResult.NotSupported("Instagram fetches media by URL: mark the media as public or use an https URL.");

        try
        {
            var user = Uri.EscapeDataString(r.ExternalProfileId);
            string containerId;
            if (r.Media.Count == 1)
            {
                var created = await CreateContainerAsync(user, r.Media[0], r.Text, carouselItem: false, r.AccessToken, ct);
                if (created.Error is not null) return created.Error;
                containerId = created.Id!;
            }
            else
            {
                var children = new List<string>();
                foreach (var m in r.Media)
                {
                    var child = await CreateContainerAsync(user, m, null, carouselItem: true, r.AccessToken, ct);
                    if (child.Error is not null) return child.Error;
                    children.Add(child.Id!);
                }
                var carousel = await graph.PostAsync($"{user}/media", new Dictionary<string, string>
                {
                    ["media_type"] = "CAROUSEL",
                    ["children"] = string.Join(',', children),
                    ["caption"] = r.Text,
                }, r.AccessToken, ct);
                if (!carousel.Success) return MetaGraphClient.Failure("Instagram", carousel);
                containerId = MetaGraphClient.Str(carousel.Body, "id")!;
            }

            if (r.Media.Any(m => m.Kind == MediaKind.Video))
            {
                var ready = await WaitForContainerAsync(containerId, r.AccessToken, ct);
                if (ready is not null) return ready;
            }

            var published = await graph.PostAsync($"{user}/media_publish",
                new Dictionary<string, string> { ["creation_id"] = containerId }, r.AccessToken, ct);
            if (!published.Success) return MetaGraphClient.Failure("Instagram", published);
            var mediaId = MetaGraphClient.Str(published.Body, "id");
            if (string.IsNullOrWhiteSpace(mediaId))
                return PublishResult.Fail(PublishFailureKind.Unknown, $"Instagram answered without a media id: {ProviderErrors.Truncate(published.Raw, 300)}");

            string? permalink = null;
            var info = await graph.GetAsync($"{Uri.EscapeDataString(mediaId)}?fields=permalink", r.AccessToken, ct);
            if (info.Success) permalink = MetaGraphClient.Str(info.Body, "permalink");

            string? note = null;
            if (!string.IsNullOrWhiteSpace(r.FirstComment))
            {
                var comment = await graph.PostAsync($"{Uri.EscapeDataString(mediaId)}/comments",
                    new Dictionary<string, string> { ["message"] = r.FirstComment }, r.AccessToken, ct);
                if (!comment.Success) note = "Published, but the first comment failed: " + ProviderErrors.Truncate(comment.Raw, 200);
            }
            return PublishResult.Ok(mediaId, permalink ?? $"https://www.instagram.com/{r.Handle.TrimStart('@')}/", note);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Instagram publish failed for variant {Variant}", r.VariantId);
            return ProviderErrors.FromException("Instagram", ex);
        }
    }

    private async Task<(string? Id, PublishResult? Error)> CreateContainerAsync(string user, PublishMedia media, string? caption,
        bool carouselItem, string token, CancellationToken ct)
    {
        var form = new Dictionary<string, string>();
        if (media.Kind == MediaKind.Video)
        {
            form["media_type"] = carouselItem ? "VIDEO" : "REELS";
            form["video_url"] = media.PublicUrl!;
        }
        else
        {
            form["image_url"] = media.PublicUrl!;
            if (!string.IsNullOrWhiteSpace(media.AltText)) form["alt_text"] = media.AltText;
        }
        if (carouselItem) form["is_carousel_item"] = "true";
        if (caption is not null) form["caption"] = caption;
        var res = await graph.PostAsync($"{user}/media", form, token, ct);
        if (!res.Success) return (null, MetaGraphClient.Failure("Instagram", res));
        var id = MetaGraphClient.Str(res.Body, "id");
        return string.IsNullOrWhiteSpace(id)
            ? (null, PublishResult.Fail(PublishFailureKind.Rejected, "Instagram did not return a container id."))
            : (id, null);
    }

    private async Task<PublishResult?> WaitForContainerAsync(string containerId, string token, CancellationToken ct)
    {
        var o = options.Value;
        for (var i = 0; i < Math.Max(1, o.InstagramMaxPolls); i++)
        {
            var status = await graph.GetAsync($"{Uri.EscapeDataString(containerId)}?fields=status_code", token, ct);
            if (!status.Success) return MetaGraphClient.Failure("Instagram", status);
            var code = MetaGraphClient.Str(status.Body, "status_code");
            if (code == "FINISHED") return null;
            if (code is "ERROR" or "EXPIRED")
                return PublishResult.Fail(PublishFailureKind.Rejected, $"Instagram could not process the video (status {code}).");
            if (o.InstagramPollSeconds > 0) await Task.Delay(TimeSpan.FromSeconds(o.InstagramPollSeconds), ct);
        }
        return PublishResult.Fail(PublishFailureKind.Transient, "Instagram is still processing the video; will retry.");
    }
}

/// <summary>
/// X API v2: <c>POST /2/tweets</c> with the user's OAuth 2.0 token (text only). A first comment is posted as a reply.
/// Media upload is not implemented (documented limitation): posts with media report NotSupported.
/// </summary>
public sealed class XPublisher(IHttpClientFactory factory, IOptions<SocialMediaOptions> options, ILogger<XPublisher> logger) : ISocialPublisher
{
    public const string HttpClientName = "x-api";

    public SocialNetwork Network => SocialNetwork.X;

    public async Task<PublishResult> PublishAsync(PublishRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.AccessToken))
            return PublishResult.NotConfigured("The X account is not connected. Connect it, or publish manually and mark as published.");
        if (r.Media.Count > 0)
            return PublishResult.NotSupported("Media upload to X is not implemented in this release; publish text-only or post manually and mark as published.");

        try
        {
            var (status, body, raw) = await PostTweetAsync(new { text = r.Text }, r.AccessToken, ct);
            if (status != HttpStatusCode.Created && status != HttpStatusCode.OK)
                return ProviderErrors.FromHttp("X", status, raw, tokenExpired: status == HttpStatusCode.Unauthorized);
            var id = body.TryGetProperty("data", out var data) ? MetaGraphClient.Str(data, "id") : null;
            if (string.IsNullOrWhiteSpace(id))
                return PublishResult.Fail(PublishFailureKind.Unknown, $"X answered without a post id: {ProviderErrors.Truncate(raw, 300)}");

            string? note = null;
            if (!string.IsNullOrWhiteSpace(r.FirstComment))
            {
                var reply = await PostTweetAsync(new { text = r.FirstComment, reply = new { in_reply_to_tweet_id = id } }, r.AccessToken, ct);
                if (reply.Status is not (HttpStatusCode.Created or HttpStatusCode.OK))
                    note = "Published, but the reply (first comment) failed: " + ProviderErrors.Truncate(reply.Raw, 200);
            }
            return PublishResult.Ok(id, $"https://x.com/{Uri.EscapeDataString(r.Handle.TrimStart('@'))}/status/{id}", note);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "X publish failed for variant {Variant}", r.VariantId);
            return ProviderErrors.FromException("X", ex);
        }
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> PostTweetAsync(object payload, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.XApiBaseUrl.TrimEnd('/')}/2/tweets")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await factory.CreateClient(HttpClientName).SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        JsonElement body;
        try
        {
            body = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            body = JsonDocument.Parse("{}").RootElement.Clone();
        }
        return (response.StatusCode, body, raw);
    }
}

/// <summary>Picks the adapter for a network (last registration wins; missing networks get a NotConfigured adapter).</summary>
public sealed class SocialPublisherRegistry(IEnumerable<ISocialPublisher> publishers)
{
    private readonly Dictionary<SocialNetwork, ISocialPublisher> _map = publishers
        .GroupBy(p => p.Network).ToDictionary(g => g.Key, g => g.Last());

    public ISocialPublisher For(SocialNetwork network) =>
        _map.TryGetValue(network, out var p) ? p : new NotConfiguredPublisher(network);
}
