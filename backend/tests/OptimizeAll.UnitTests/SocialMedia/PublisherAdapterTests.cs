using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.UnitTests.SocialMedia;

public sealed class RecordingHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string Body, string? Auth)> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.Authorization?.ToString()));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public sealed class PublisherAdapterTests
{
    private static readonly IOptions<SocialMediaOptions> Options = Microsoft.Extensions.Options.Options.Create(new SocialMediaOptions
    {
        GraphApiBaseUrl = "https://graph.test/v20.0", XApiBaseUrl = "https://x.test", InstagramPollSeconds = 0, InstagramMaxPolls = 3,
    });

    private static PublishRequest Request(SocialNetwork network, string text = "Hello world", IReadOnlyList<PublishMedia>? media = null,
        string? token = "tok", string? external = "123", string? firstComment = null, string? link = null) =>
        new(Guid.NewGuid(), network, "brand", external, token, text, null, link, firstComment, media ?? Array.Empty<PublishMedia>());

    private static FacebookPagePublisher Facebook(RecordingHandler h) =>
        new(new MetaGraphClient(new SingleHandlerFactory(h), Options), NullLogger<FacebookPagePublisher>.Instance);

    private static InstagramPublisher Instagram(RecordingHandler h) =>
        new(new MetaGraphClient(new SingleHandlerFactory(h), Options), Options, NullLogger<InstagramPublisher>.Instance);

    private static XPublisher X(RecordingHandler h) => new(new SingleHandlerFactory(h), Options, NullLogger<XPublisher>.Instance);

    [Fact]
    public async Task Facebook_text_post_goes_to_the_page_feed_and_returns_the_post_id()
    {
        var h = new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"123_456\"}"));
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook, link: "https://example.com"), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("123_456", result.ExternalPostId);
        Assert.Equal("https://www.facebook.com/123_456", result.Url);
        var (method, url, body, auth) = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://graph.test/v20.0/123/feed", url);
        Assert.Contains("message=Hello+world", body);
        Assert.Contains("link=https", body);
        Assert.Equal("Bearer tok", auth);
    }

    [Fact]
    public async Task Facebook_single_photo_uses_photos_endpoint_and_first_comment()
    {
        var h = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/photos")
            ? RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"p1\",\"post_id\":\"123_789\"}")
            : RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"c1\"}"));
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook, media: new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", "alt") },
            firstComment: "First!"), default);
        Assert.Equal("123_789", result.ExternalPostId);
        Assert.Equal(2, h.Requests.Count);
        Assert.EndsWith("/123_789/comments", h.Requests[1].Url);
    }

    [Fact]
    public async Task Facebook_expired_token_is_an_authorization_failure()
    {
        var h = new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Error validating access token: Session has expired\",\"type\":\"OAuthException\",\"code\":190,\"error_subcode\":463}}"));
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook), default);
        Assert.Equal(PublishOutcome.Failed, result.Outcome);
        Assert.Equal(PublishFailureKind.Authorization, result.FailureKind);
        Assert.Null(result.ExternalPostId);
    }

    [Fact]
    public async Task Facebook_server_error_and_rate_limit_are_transient_and_rejections_are_not()
    {
        var server = await Facebook(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.InternalServerError, "{}"))).PublishAsync(Request(SocialNetwork.Facebook), default);
        Assert.Equal(PublishFailureKind.Transient, server.FailureKind);
        var limited = await Facebook(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Application request limit reached\",\"code\":4}}"))).PublishAsync(Request(SocialNetwork.Facebook), default);
        Assert.Equal(PublishFailureKind.Transient, limited.FailureKind);
        var rejected = await Facebook(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"Invalid parameter\",\"code\":100}}"))).PublishAsync(Request(SocialNetwork.Facebook), default);
        Assert.Equal(PublishFailureKind.Rejected, rejected.FailureKind);
    }

    [Fact]
    public async Task Facebook_without_token_is_not_configured_and_sends_nothing()
    {
        var h = new RecordingHandler((_, _) => throw new InvalidOperationException("must not be called"));
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook, token: null), default);
        Assert.Equal(PublishOutcome.NotConfigured, result.Outcome);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Instagram_creates_a_container_then_publishes_it()
    {
        var h = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v20.0/123/media" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"container-1\"}"),
            "/v20.0/123/media_publish" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"media-9\"}"),
            "/v20.0/media-9" => RecordingHandler.Json(HttpStatusCode.OK, "{\"permalink\":\"https://www.instagram.com/p/abc/\",\"id\":\"media-9\"}"),
            _ => RecordingHandler.Json(HttpStatusCode.NotFound, "{}"),
        });
        var result = await Instagram(h).PublishAsync(Request(SocialNetwork.Instagram, media: new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", "A runner") }), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("media-9", result.ExternalPostId);
        Assert.Equal("https://www.instagram.com/p/abc/", result.Url);
        Assert.Contains("image_url=https", h.Requests[0].Body);
        Assert.Contains("caption=Hello+world", h.Requests[0].Body);
        Assert.Contains("creation_id=container-1", h.Requests[1].Body);
    }

    [Fact]
    public async Task Instagram_video_waits_for_the_container_to_finish()
    {
        var polls = 0;
        var h = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v20.0/123/media" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"c-video\"}"),
            "/v20.0/c-video" => RecordingHandler.Json(HttpStatusCode.OK, ++polls < 2 ? "{\"status_code\":\"IN_PROGRESS\"}" : "{\"status_code\":\"FINISHED\"}"),
            "/v20.0/123/media_publish" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"m-1\"}"),
            _ => RecordingHandler.Json(HttpStatusCode.OK, "{}"),
        });
        var result = await Instagram(h).PublishAsync(Request(SocialNetwork.Instagram, media: new[] { new PublishMedia(MediaKind.Video, "https://cdn.test/v.mp4", null) }), default);
        Assert.Equal("m-1", result.ExternalPostId);
        Assert.Contains("media_type=REELS", h.Requests[0].Body);
        Assert.Equal(2, polls);
    }

    [Fact]
    public async Task Instagram_needs_publicly_reachable_media()
    {
        var h = new RecordingHandler((_, _) => throw new InvalidOperationException("must not be called"));
        var result = await Instagram(h).PublishAsync(Request(SocialNetwork.Instagram, media: new[] { new PublishMedia(MediaKind.Image, null, null) }), default);
        Assert.Equal(PublishOutcome.NotSupported, result.Outcome);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task X_posts_text_with_v2_tweets_and_replies_with_the_first_comment()
    {
        var h = new RecordingHandler((_, body) => RecordingHandler.Json(HttpStatusCode.Created,
            body.Contains("in_reply_to_tweet_id") ? "{\"data\":{\"id\":\"2\",\"text\":\"c\"}}" : "{\"data\":{\"id\":\"1789\",\"text\":\"Hello world\"}}"));
        var result = await X(h).PublishAsync(Request(SocialNetwork.X, firstComment: "More below"), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("1789", result.ExternalPostId);
        Assert.Equal("https://x.com/brand/status/1789", result.Url);
        Assert.Equal("https://x.test/2/tweets", h.Requests[0].Url);
        Assert.Contains("\"text\":\"Hello world\"", h.Requests[0].Body);
        Assert.Contains("\"in_reply_to_tweet_id\":\"1789\"", h.Requests[1].Body);
    }

    [Fact]
    public async Task X_unauthorized_is_authorization_and_forbidden_is_rejected()
    {
        var unauthorized = await X(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.Unauthorized, "{\"title\":\"Unauthorized\"}")))
            .PublishAsync(Request(SocialNetwork.X), default);
        Assert.Equal(PublishFailureKind.Authorization, unauthorized.FailureKind);
        var duplicate = await X(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.Forbidden, "{\"detail\":\"duplicate content\"}")))
            .PublishAsync(Request(SocialNetwork.X), default);
        Assert.Equal(PublishFailureKind.Rejected, duplicate.FailureKind);
        var limited = await X(new RecordingHandler((_, _) => RecordingHandler.Json(HttpStatusCode.TooManyRequests, "{}"))).PublishAsync(Request(SocialNetwork.X), default);
        Assert.Equal(PublishFailureKind.Transient, limited.FailureKind);
    }

    [Fact]
    public async Task X_media_upload_is_not_supported_and_not_attempted()
    {
        var h = new RecordingHandler((_, _) => throw new InvalidOperationException("must not be called"));
        var result = await X(h).PublishAsync(Request(SocialNetwork.X, media: new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", null) }), default);
        Assert.Equal(PublishOutcome.NotSupported, result.Outcome);
        Assert.Empty(h.Requests);
    }

    [Theory]
    [InlineData(SocialNetwork.LinkedIn)]
    [InlineData(SocialNetwork.TikTok)]
    [InlineData(SocialNetwork.YouTube)]
    [InlineData(SocialNetwork.Pinterest)]
    [InlineData(SocialNetwork.GoogleBusiness)]
    public async Task Networks_without_an_adapter_report_not_configured(SocialNetwork network)
    {
        var registry = new SocialPublisherRegistry(Array.Empty<ISocialPublisher>());
        var result = await registry.For(network).PublishAsync(Request(network), default);
        Assert.Equal(PublishOutcome.NotConfigured, result.Outcome);
        Assert.Null(result.ExternalPostId);
        Assert.Contains("Mark as published", result.Message);
    }
}
