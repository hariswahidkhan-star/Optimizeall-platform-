using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;

namespace OptimizeAll.UnitTests.SocialMedia;

public sealed class PostValidatorTests
{
    private static readonly MediaInfo Square = new(Guid.NewGuid(), MediaKind.Image, 1080, 1080, null, 400_000, true);
    private static readonly MediaInfo Portrait = new(Guid.NewGuid(), MediaKind.Image, 1080, 1350, null, 400_000, true);
    private static readonly MediaInfo TooTall = new(Guid.NewGuid(), MediaKind.Image, 1080, 1920, null, 400_000, true);
    private static readonly MediaInfo Video = new(Guid.NewGuid(), MediaKind.Video, 1080, 1920, 45, null, true);

    private static VariantContent Content(SocialNetwork n, string text, IReadOnlyList<MediaInfo>? media = null, string? link = null,
        string? title = null, string? firstComment = null, IReadOnlyList<string>? hashtags = null, IReadOnlyList<string>? alt = null) =>
        new(n, text, title, media ?? Array.Empty<MediaInfo>(), alt ?? Array.Empty<string>(), link, firstComment, hashtags ?? Array.Empty<string>(),
            Array.Empty<string>());

    private static VariantValidation Validate(VariantContent c) => PostValidator.Validate(c, NetworkPresets.Default(c.Network));

    private static IEnumerable<string> Errors(VariantValidation v) => v.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Code);

    [Fact]
    public void X_counts_urls_as_23_and_accepts_exactly_280()
    {
        var text = new string('a', 280 - 24) + " https://example.com/a/very/long/path/that/is/much/longer/than/twenty-three";
        var result = Validate(Content(SocialNetwork.X, text));
        Assert.Equal(280, result.TextLength);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void X_rejects_281_weighted_characters_and_counts_cjk_and_emoji_as_two()
    {
        Assert.Contains("social.text_too_long", Errors(Validate(Content(SocialNetwork.X, new string('b', 281)))));
        Assert.Equal(4, PostValidator.TextLength("日本", PostValidator.XUrlWeight));
        Assert.Equal(2, PostValidator.TextLength("👍🏽", PostValidator.XUrlWeight));
        Assert.Equal(2, PostValidator.TextLength("é!", PostValidator.XUrlWeight));
    }

    [Fact]
    public void X_appends_the_link_to_the_text_but_facebook_keeps_it_as_an_attachment()
    {
        var x = Validate(Content(SocialNetwork.X, "Read this", link: "https://example.com/post"));
        Assert.EndsWith("https://example.com/post", x.FinalText);
        Assert.Equal(9 + 1 + 23, x.TextLength);
        var fb = Validate(Content(SocialNetwork.Facebook, "Read this", link: "https://example.com/post"));
        Assert.Equal("Read this", fb.FinalText);
    }

    [Fact]
    public void Instagram_requires_media_limits_hashtags_and_checks_aspect_ratio()
    {
        Assert.Contains("social.media_required", Errors(Validate(Content(SocialNetwork.Instagram, "Hello"))));
        var tags = Enumerable.Range(1, 31).Select(i => $"#tag{i}").ToList();
        Assert.Contains("social.too_many_hashtags", Errors(Validate(Content(SocialNetwork.Instagram, "Hi", new[] { Portrait }, hashtags: tags))));
        Assert.Contains("social.aspect_ratio", Errors(Validate(Content(SocialNetwork.Instagram, "Hi", new[] { TooTall }, alt: new[] { "x" }))));
        var ok = Validate(Content(SocialNetwork.Instagram, "Hi", new[] { Portrait }, alt: new[] { "A person running" }));
        Assert.True(ok.IsValid);
        Assert.Empty(ok.Issues);
    }

    [Fact]
    public void Instagram_warns_that_caption_links_are_not_clickable_and_about_missing_alt_text()
    {
        var result = Validate(Content(SocialNetwork.Instagram, "Hi", new[] { Square }, link: "https://example.com"));
        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, i => i.Code == "social.link_not_clickable" && i.Severity == IssueSeverity.Warning);
        Assert.Contains(result.Issues, i => i.Code == "social.alt_text_missing");
    }

    [Fact]
    public void Hashtags_are_appended_once_and_counted_with_those_in_the_text()
    {
        var composed = PostValidator.ComposeText("Morning run #Fitness", new[] { "fitness", "#Run", "Morning Run" }, null, LinkHandling.Attachment);
        Assert.Equal("Morning run #Fitness\n\n#Run #MorningRun", composed);
        Assert.Equal(3, PostValidator.CountHashtags(composed));
    }

    [Fact]
    public void Media_count_type_and_mixing_rules_per_network()
    {
        var five = Enumerable.Repeat(Square, 5).ToList();
        Assert.Contains("social.too_many_media", Errors(Validate(Content(SocialNetwork.X, "Hi", five))));
        Assert.Contains("social.mixed_media", Errors(Validate(Content(SocialNetwork.X, "Hi", new[] { Square, Video }))));
        Assert.Contains("social.too_many_videos", Errors(Validate(Content(SocialNetwork.GoogleBusiness, "Hi", new[] { Video }))));
        Assert.Contains("social.video_required", Errors(Validate(Content(SocialNetwork.YouTube, "Hi", new[] { Square }, title: "t"))));
        Assert.True(Validate(Content(SocialNetwork.Instagram, "Carousel", new[] { Square, Video }, alt: new[] { "a" })).IsValid);
    }

    [Fact]
    public void Title_rules_youtube_requires_one_and_pinterest_limits_it()
    {
        Assert.Contains("social.title_required", Errors(Validate(Content(SocialNetwork.YouTube, "Desc", new[] { Video }))));
        Assert.Contains("social.title_too_long", Errors(Validate(Content(SocialNetwork.Pinterest, "Desc", new[] { Square }, title: new string('t', 101)))));
        Assert.Contains(Validate(Content(SocialNetwork.X, "Hi", title: "ignored")).Issues, i => i.Code == "social.title_ignored");
    }

    [Fact]
    public void First_comment_is_rejected_where_unsupported_and_links_must_be_http()
    {
        Assert.Contains("social.first_comment_unsupported", Errors(Validate(Content(SocialNetwork.TikTok, "Hi", new[] { Video }, firstComment: "c"))));
        Assert.Contains("social.invalid_link", Errors(Validate(Content(SocialNetwork.Facebook, "Hi", link: "javascript:alert(1)"))));
        Assert.Contains("social.empty", Errors(Validate(Content(SocialNetwork.Facebook, "   "))));
    }

    [Fact]
    public void Video_duration_outside_limits_is_an_error_and_unknown_duration_a_warning()
    {
        var longVideo = Video with { DurationSeconds = 200 };
        Assert.Contains("social.video_duration", Errors(Validate(Content(SocialNetwork.X, "Hi", new[] { longVideo }))));
        var unknown = Validate(Content(SocialNetwork.X, "Hi", new[] { Video with { DurationSeconds = null } }));
        Assert.True(unknown.IsValid);
        Assert.Contains(unknown.Issues, i => i.Code == "social.video_duration_unknown");
    }

    [Fact]
    public void Every_network_has_a_default_preset_with_recommended_times()
    {
        foreach (var network in Enum.GetValues<SocialNetwork>())
        {
            var preset = NetworkPresets.Default(network);
            Assert.NotEmpty(preset.RecommendedTimes);
            Assert.All(preset.RecommendedTimes, t => Assert.NotNull(NetworkPresets.ParseTime(t)));
            Assert.False(string.IsNullOrWhiteSpace(preset.Source));
        }
    }
}

public sealed class PostWorkflowTests
{
    [Fact]
    public void Client_approval_gate_is_inserted_only_when_the_client_requires_it()
    {
        Assert.Equal(SocialPostStatus.ClientApproval, PostWorkflow.Next(SocialPostStatus.InternalReview, WorkflowAction.ApproveInternal, true));
        Assert.Equal(SocialPostStatus.Approved, PostWorkflow.Next(SocialPostStatus.InternalReview, WorkflowAction.ApproveInternal, false));
        Assert.Equal(SocialPostStatus.Approved, PostWorkflow.Next(SocialPostStatus.ClientApproval, WorkflowAction.ClientApprove, true));
        Assert.Equal(SocialPostStatus.Draft, PostWorkflow.Next(SocialPostStatus.ClientApproval, WorkflowAction.ClientRequestChanges, true));
    }

    [Theory]
    [InlineData(SocialPostStatus.Draft, WorkflowAction.Schedule)]
    [InlineData(SocialPostStatus.InternalReview, WorkflowAction.Schedule)]
    [InlineData(SocialPostStatus.ClientApproval, WorkflowAction.Schedule)]
    [InlineData(SocialPostStatus.Draft, WorkflowAction.ClientApprove)]
    [InlineData(SocialPostStatus.Published, WorkflowAction.Retry)]
    [InlineData(SocialPostStatus.Scheduled, WorkflowAction.Submit)]
    public void Posts_cannot_skip_the_workflow(SocialPostStatus from, WorkflowAction action)
    {
        var ex = Assert.Throws<DomainException>(() => PostWorkflow.Next(from, action, true));
        Assert.Equal("social.invalid_transition", ex.Code);
        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
    }

    [Fact]
    public void Aggregate_and_retry_backoff()
    {
        Assert.Equal(SocialPostStatus.Published, PostWorkflow.Aggregate(new[] { VariantPublishStatus.Published, VariantPublishStatus.Published }, false));
        Assert.Equal(SocialPostStatus.Scheduled, PostWorkflow.Aggregate(new[] { VariantPublishStatus.Published, VariantPublishStatus.Pending }, true));
        Assert.Equal(SocialPostStatus.Failed, PostWorkflow.Aggregate(new[] { VariantPublishStatus.Published, VariantPublishStatus.Failed }, false));
        Assert.Equal(TimeSpan.FromMinutes(2), PostWorkflow.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(10), PostWorkflow.RetryDelay(2));
        Assert.Null(PostWorkflow.RetryDelay(4));
    }

    [Fact]
    public void Evergreen_requeues_on_cadence_and_stops_at_the_maximum()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1, EvergreenRules.DueRepeat(true, 0, 3, 30, now.AddDays(-30), now));
        Assert.Null(EvergreenRules.DueRepeat(true, 0, 3, 30, now.AddDays(-29), now));
        Assert.Equal(3, EvergreenRules.DueRepeat(true, 2, 3, 30, now.AddDays(-40), now));
        Assert.Null(EvergreenRules.DueRepeat(true, 3, 3, 30, now.AddDays(-400), now));
        Assert.Null(EvergreenRules.DueRepeat(false, 0, 3, 30, now.AddDays(-400), now));
        Assert.Null(EvergreenRules.DueRepeat(true, 0, 3, 30, null, now));
    }

    [Fact]
    public void Queue_picks_the_next_free_slot_in_the_client_time_zone()
    {
        var karachi = QueueScheduler.Zone("Asia/Karachi"); // UTC+5, no DST
        var slots = new[] { (DayOfWeek.Wednesday, 9 * 60), (DayOfWeek.Wednesday, 18 * 60) };
        var after = new DateTime(2026, 9, 23, 5, 0, 0, DateTimeKind.Utc); // Wed 10:00 local
        var first = QueueScheduler.NextFreeSlot(slots, karachi, after, Array.Empty<DateTime>());
        Assert.Equal(new DateTime(2026, 9, 23, 13, 0, 0, DateTimeKind.Utc), first); // Wed 18:00 local
        var second = QueueScheduler.NextFreeSlot(slots, karachi, after, new[] { first!.Value });
        Assert.Equal(new DateTime(2026, 9, 30, 4, 0, 0, DateTimeKind.Utc), second); // next Wed 09:00 local
        Assert.Null(QueueScheduler.NextFreeSlot(Array.Empty<(DayOfWeek, int)>(), karachi, after, Array.Empty<DateTime>()));
    }
}

public sealed class SentimentScorerTests
{
    [Theory]
    [InlineData("I love this app, the workouts are amazing!", Sentiment.Positive)]
    [InlineData("Terrible service, the delivery was late and cold.", Sentiment.Negative)]
    [InlineData("The app is not good at all", Sentiment.Negative)]
    [InlineData("Is this available in Lahore?", Sentiment.Neutral)]
    [InlineData("Best biryani in town 😍", Sentiment.Positive)]
    [InlineData("👎👎", Sentiment.Negative)]
    public void Scores_common_phrases(string text, Sentiment expected) => Assert.Equal(expected, SentimentScorer.Score(text).Sentiment);

    [Fact]
    public void Score_is_bounded_and_empty_text_is_neutral()
    {
        var s = SentimentScorer.Score("very very amazing amazing love love");
        Assert.InRange(s.Score, -1m, 1m);
        Assert.Equal(0, SentimentScorer.Score("").MatchedTerms);
        Assert.Equal(Sentiment.Neutral, SentimentScorer.Score(null).Sentiment);
    }
}

public sealed class OAuthStateTests
{
    private static readonly byte[] Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.NewGuid();

    private static string Signed(DateTime? expires = null, Guid? user = null) =>
        OAuthState.Sign(new OAuthStatePayload("social-profile", Guid.NewGuid(), user ?? User, expires ?? Now.AddMinutes(15), OAuthState.NewNonce()), Key);

    [Fact]
    public void Valid_state_round_trips()
    {
        var state = Signed();
        var payload = OAuthState.Verify(state, Key, User, Now, out var reason);
        Assert.NotNull(payload);
        Assert.Null(reason);
        Assert.Equal("social-profile", payload!.Purpose);
    }

    [Fact]
    public void Tampered_body_or_signature_is_rejected()
    {
        var state = Signed();
        var dot = state.IndexOf('.');
        var body = OAuthState.FromBase64Url(state[..dot])!;
        body[^1] ^= 0x01;
        var tamperedBody = OAuthState.Base64Url(body) + state[dot..];
        Assert.Null(OAuthState.Verify(tamperedBody, Key, User, Now, out var r1));
        Assert.Equal("signature", r1);

        var sig = OAuthState.FromBase64Url(state[(dot + 1)..])!;
        sig[0] ^= 0x80;
        Assert.Null(OAuthState.Verify(state[..(dot + 1)] + OAuthState.Base64Url(sig), Key, User, Now, out var r2));
        Assert.Equal("signature", r2);

        Assert.Null(OAuthState.Verify(state, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), User, Now, out var r3));
        Assert.Equal("signature", r3);
    }

    [Fact]
    public void Expired_other_user_and_malformed_states_are_rejected()
    {
        Assert.Null(OAuthState.Verify(Signed(expires: Now.AddSeconds(-1)), Key, User, Now, out var expired));
        Assert.Equal("expired", expired);
        Assert.Null(OAuthState.Verify(Signed(user: Guid.NewGuid()), Key, User, Now, out var other));
        Assert.Equal("user", other);
        Assert.Null(OAuthState.Verify("not-a-state", Key, User, Now, out var malformed));
        Assert.Equal("malformed", malformed);
        Assert.Null(OAuthState.Verify(null, Key, User, Now, out _));
    }

    [Fact]
    public void Pkce_verifier_is_deterministic_per_nonce_and_challenge_is_s256()
    {
        var v1 = OAuthState.PkceVerifier(Key, "nonce-1");
        Assert.Equal(v1, OAuthState.PkceVerifier(Key, "nonce-1"));
        Assert.NotEqual(v1, OAuthState.PkceVerifier(Key, "nonce-2"));
        Assert.InRange(v1.Length, 43, 128);
        Assert.Equal(43, OAuthState.PkceChallenge(v1).Length);
    }
}
