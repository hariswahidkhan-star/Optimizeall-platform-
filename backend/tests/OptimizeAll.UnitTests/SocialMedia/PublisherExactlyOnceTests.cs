using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.UnitTests.SocialMedia;

/// <summary>
/// A publish whose outcome is ambiguous must never be classified as Transient (which the publishing job retries
/// automatically, producing a duplicate post), and a failing follow-up step after the post is live must not turn the
/// publication into a failure.
/// </summary>
public sealed class PublisherExactlyOnceTests
{
    private static readonly IOptions<SocialMediaOptions> Options = Microsoft.Extensions.Options.Options.Create(new SocialMediaOptions
    {
        GraphApiBaseUrl = "https://graph.test/v20.0", XApiBaseUrl = "https://x.test", InstagramPollSeconds = 0, InstagramMaxPolls = 3,
    });

    private static PublishRequest Request(SocialNetwork network, IReadOnlyList<PublishMedia>? media = null, string? firstComment = null) =>
        new(Guid.NewGuid(), network, "brand", "123", "tok", "Hello world", null, null, firstComment, media ?? Array.Empty<PublishMedia>());

    private static FacebookPagePublisher Facebook(RecordingHandler h) =>
        new(new MetaGraphClient(new SingleHandlerFactory(h), Options), NullLogger<FacebookPagePublisher>.Instance);

    private static InstagramPublisher Instagram(RecordingHandler h) =>
        new(new MetaGraphClient(new SingleHandlerFactory(h), Options), Options, NullLogger<InstagramPublisher>.Instance);

    private static XPublisher X(RecordingHandler h) => new(new SingleHandlerFactory(h), Options, NullLogger<XPublisher>.Instance);

    private static Exception Timeout() => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");

    [Fact]
    public async Task Facebook_timeout_on_the_publish_request_is_unknown_not_transient()
    {
        var h = new RecordingHandler((_, _) => throw Timeout());
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook), default);
        Assert.Equal(PublishOutcome.Failed, result.Outcome);
        Assert.Equal(PublishFailureKind.Unknown, result.FailureKind);
    }

    [Fact]
    public async Task Facebook_unpublished_photo_upload_failure_is_still_transient()
    {
        // Multi-photo posts first upload unpublished photos: nothing is live yet, so a retry is safe.
        var h = new RecordingHandler((_, _) => throw new HttpRequestException("reset"));
        var media = new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", null), new PublishMedia(MediaKind.Image, "https://cdn.test/b.jpg", null) };
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook, media), default);
        Assert.Equal(PublishFailureKind.Transient, result.FailureKind);
    }

    [Fact]
    public async Task Facebook_first_comment_network_error_keeps_the_post_published()
    {
        var h = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/comments")
            ? throw Timeout()
            : RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"123_456\"}"));
        var result = await Facebook(h).PublishAsync(Request(SocialNetwork.Facebook, firstComment: "First!"), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("123_456", result.ExternalPostId);
        Assert.Contains("first comment", result.Message);
    }

    [Fact]
    public async Task Instagram_timeout_on_media_publish_is_unknown_and_on_container_creation_is_transient()
    {
        var media = new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", null) };
        var publishTimeout = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath.EndsWith("/media_publish")
            ? throw Timeout()
            : RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"container-1\"}"));
        var ambiguous = await Instagram(publishTimeout).PublishAsync(Request(SocialNetwork.Instagram, media), default);
        Assert.Equal(PublishFailureKind.Unknown, ambiguous.FailureKind);

        var containerTimeout = new RecordingHandler((_, _) => throw Timeout());
        var safe = await Instagram(containerTimeout).PublishAsync(Request(SocialNetwork.Instagram, media), default);
        Assert.Equal(PublishFailureKind.Transient, safe.FailureKind);
    }

    [Fact]
    public async Task Instagram_follow_up_failures_after_publishing_keep_the_post_published()
    {
        var media = new[] { new PublishMedia(MediaKind.Image, "https://cdn.test/a.jpg", null) };
        var h = new RecordingHandler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v20.0/123/media" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"container-1\"}"),
            "/v20.0/123/media_publish" => RecordingHandler.Json(HttpStatusCode.OK, "{\"id\":\"media-9\"}"),
            _ => throw new HttpRequestException("connection reset"),
        });
        var result = await Instagram(h).PublishAsync(Request(SocialNetwork.Instagram, media, firstComment: "Hi"), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("media-9", result.ExternalPostId);
        Assert.Equal("https://www.instagram.com/brand/", result.Url);
    }

    [Fact]
    public async Task X_timeout_is_unknown_but_a_refused_connection_is_transient()
    {
        var timeout = await X(new RecordingHandler((_, _) => throw Timeout())).PublishAsync(Request(SocialNetwork.X), default);
        Assert.Equal(PublishFailureKind.Unknown, timeout.FailureKind);

        var refused = await X(new RecordingHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused",
            new SocketException((int)SocketError.ConnectionRefused)))).PublishAsync(Request(SocialNetwork.X), default);
        Assert.Equal(PublishFailureKind.Transient, refused.FailureKind);
    }

    [Fact]
    public async Task X_reply_network_error_keeps_the_post_published()
    {
        var calls = 0;
        var h = new RecordingHandler((_, _) => ++calls == 1
            ? RecordingHandler.Json(HttpStatusCode.Created, "{\"data\":{\"id\":\"42\",\"text\":\"Hello world\"}}")
            : throw Timeout());
        var result = await X(h).PublishAsync(Request(SocialNetwork.X, firstComment: "More below"), default);
        Assert.Equal(PublishOutcome.Published, result.Outcome);
        Assert.Equal("42", result.ExternalPostId);
    }
}
