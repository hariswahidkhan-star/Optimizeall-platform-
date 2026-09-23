using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.UnitTests.Marketing;

public sealed class ReferralFraudRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<string> Evaluate(
        string referrer = "owner@example.com", string referred = "friend@example.com", string? ip = "ip-new", string? device = "dev-new",
        IReadOnlyCollection<PriorReferral>? prior = null, string? ownIp = null, string? ownDevice = null) =>
        ReferralFraudRules.Evaluate(new ReferralFraudContext(referrer, referred, ip, device, prior ?? Array.Empty<PriorReferral>(), Now, ownIp, ownDevice));

    [Fact]
    public void Clean_referral_has_no_signals() => Assert.Empty(Evaluate());

    [Fact]
    public void Shared_ip_within_30_days_is_flagged_but_not_older()
    {
        Assert.Contains(ReferralFraudSignals.SharedIp, Evaluate(prior: new[] { new PriorReferral("ip-new", "x", Now.AddDays(-29)) }));
        Assert.DoesNotContain(ReferralFraudSignals.SharedIp, Evaluate(prior: new[] { new PriorReferral("ip-new", "x", Now.AddDays(-31)) }));
        Assert.DoesNotContain(ReferralFraudSignals.SharedIp, Evaluate(ip: null, prior: new[] { new PriorReferral(null, "x", Now) }));
    }

    [Fact]
    public void Shared_device_with_another_referral_or_the_referrer_is_flagged()
    {
        Assert.Contains(ReferralFraudSignals.SharedDevice, Evaluate(prior: new[] { new PriorReferral("other", "dev-new", Now.AddDays(-200)) }));
        Assert.Contains(ReferralFraudSignals.SharedDevice, Evaluate(ownDevice: "dev-new"));
        Assert.Contains(ReferralFraudSignals.SharedIp, Evaluate(ownIp: "ip-new"));
        Assert.DoesNotContain(ReferralFraudSignals.SharedDevice, Evaluate(device: null, prior: new[] { new PriorReferral("a", null, Now) }));
    }

    [Theory]
    [InlineData("sara@example.com", "sara+promo@example.com", true)]
    [InlineData("Sara@Example.com", "sara@example.com", true)]
    [InlineData("sara.khan@gmail.com", "sarakhan+x@googlemail.com", true)]
    [InlineData("s.a.r.a@gmail.com", "sara@gmail.com", true)]
    [InlineData("sara.khan@example.com", "sarakhan@example.com", false)]
    [InlineData("sara@example.com", "sara@example.org", false)]
    public void Email_alias_detection(string referrer, string referred, bool alias)
    {
        Assert.Equal(alias, ReferralFraudRules.IsEmailAlias(referrer, referred));
        Assert.Equal(alias, Evaluate(referrer: referrer, referred: referred).Contains(ReferralFraudSignals.EmailAlias));
    }

    [Fact]
    public void Velocity_flags_more_than_ten_referrals_in_24_hours()
    {
        var nine = Enumerable.Range(0, 9).Select(i => new PriorReferral($"ip{i}", $"d{i}", Now.AddHours(-i))).ToList();
        Assert.DoesNotContain(ReferralFraudSignals.Velocity, Evaluate(prior: nine)); // 10 including this one
        var ten = nine.Append(new PriorReferral("ipX", "dX", Now.AddHours(-23))).ToList();
        Assert.Contains(ReferralFraudSignals.Velocity, Evaluate(prior: ten)); // 11
        var tenButOld = nine.Append(new PriorReferral("ipX", "dX", Now.AddHours(-25))).ToList();
        Assert.DoesNotContain(ReferralFraudSignals.Velocity, Evaluate(prior: tenButOld));
    }

    [Theory]
    [InlineData("x@mailinator.com", true)]
    [InlineData("x@MAILINATOR.COM", true)]
    [InlineData("x@eu.mailinator.com", true)]
    [InlineData("x@yopmail.com", true)]
    [InlineData("x@10minutemail.com", true)]
    [InlineData("x@notmailinator.com", false)]
    [InlineData("x@gmail.com", false)]
    public void Disposable_domains(string email, bool disposable)
    {
        Assert.Equal(disposable, ReferralFraudRules.IsDisposable(email));
        Assert.Equal(disposable, Evaluate(referred: email).Contains(ReferralFraudSignals.DisposableEmail));
    }

    [Fact]
    public void Multiple_signals_are_reported_together()
    {
        var signals = Evaluate(referrer: "sara@gmail.com", referred: "s.ara+1@gmail.com",
            prior: new[] { new PriorReferral("ip-new", "dev-new", Now.AddHours(-1)) });
        Assert.Equal(new[] { ReferralFraudSignals.SharedIp, ReferralFraudSignals.SharedDevice, ReferralFraudSignals.EmailAlias }, signals);
    }

    [Fact]
    public void Signals_round_trip_through_storage_format()
    {
        var stored = ReferralFraudRules.Join(new[] { "shared_ip", "velocity" });
        Assert.Equal("shared_ip,velocity", stored);
        Assert.Equal(new[] { "shared_ip", "velocity" }, ReferralFraudRules.Split(stored));
        Assert.Null(ReferralFraudRules.Join(Array.Empty<string>()));
        Assert.Empty(ReferralFraudRules.Split(null));
    }
}
