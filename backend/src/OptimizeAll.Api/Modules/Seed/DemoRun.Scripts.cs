using OptimizeAll.Domain.Common;
using S = OptimizeAll.Api.Modules.Seed.StepKind;
using P = OptimizeAll.Domain.Common.SocialPlatform;

namespace OptimizeAll.Api.Modules.Seed;

/// <summary>
/// Hand-written histories for the people the demo scripts talk about. Times are anchored to the payout periods
/// (c3 … c0 = cutoffs of the last four completed periods) so the same story plays out whatever day the seed runs.
/// </summary>
internal sealed partial class DemoRun
{
    private string? _saraTicketReference;

    private void PlanScriptedHistories()
    {
        var nimbus = C("nimbus");
        var leaf = C("leaf");
        var eats = C("eats");
        var bloom = C("bloom");
        var wander = C("wander");
        var aurora = C("aurora");
        var c3 = _p3.CutoffUtc;
        var c2 = _p2.CutoffUtc;
        var c1 = _p1.CutoffUtc;
        var c0 = _p0.CutoffUtc;
        var v2 = nimbus.RuleSets[1].EffectiveFrom;
        DateTime Ago(double days) => _now.AddDays(-days);

        // ---------------------------------------------------------------- Sara: the participant journey
        var sara = Sara;
        var s1 = Plan(sara, leaf, P.X, c3.AddDays(-9).AddHours(-8));
        s1.Then(S.Approve, s1.SubmittedAt.AddHours(10), Reviewer1, "Clear disclosure and tracking link — approved.");
        var s2 = Plan(sara, leaf, P.Instagram, c3.AddDays(-6).AddHours(-8));
        s2.Then(S.Approve, s2.SubmittedAt.AddHours(20), Reviewer2);
        // Launch week (time-limited bonus) + first-post bonus, priced with reward rules v1.
        var s3 = Plan(sara, nimbus, P.Instagram, nimbus.Campaign.StartsAt.AddDays(2).AddHours(3), postedBefore: TimeSpan.FromHours(2));
        s3.Then(S.Approve, s3.SubmittedAt.AddHours(8), Reviewer1, "Great launch-week post.");
        // Submitted under v1, approved after v2 went live: still paid at the v1 TikTok rate (historical rate preservation).
        var s4 = Plan(sara, nimbus, P.TikTok, v2.AddDays(-3));
        s4.Then(S.Approve, v2.AddDays(1), Reviewer2);
        var s13 = Plan(sara, leaf, P.X, c1.AddDays(-6));
        s13.Then(S.Approve, c1.AddDays(-5), Reviewer1);
        var s11 = Plan(sara, eats, P.TikTok, Ago(20));
        s11.Then(S.Approve, Ago(19), Reviewer2).Then(S.LiveConfirm, s11.PostedAt.AddHours(52), Reviewer1);
        var s5 = Plan(sara, wander, P.Instagram, Ago(25));
        s5.Then(S.Approve, Ago(24), Reviewer1).Then(S.Reverse, Ago(22), Reviewer2, "The disclosure was removed from the caption after approval.");
        var s12 = Plan(sara, nimbus, P.TikTok, c0.AddDays(-6));
        s12.Then(S.Approve, c0.AddDays(-5), Reviewer2);
        var s10 = Plan(sara, nimbus, P.Instagram, c0.AddDays(-3));
        s10.Then(S.Approve, c0.AddDays(-2), Reviewer1);
        var s9 = Plan(sara, wander, P.TikTok, Ago(6));
        s9.Then(S.RequestCorrection, Ago(5), Reviewer1, "Please add the disclosure \"#ad Paid partnership with Wanderly\" to the caption and resubmit.");
        var s7 = Plan(sara, nimbus, P.X, Ago(4));
        s7.Then(S.Approve, Ago(2), Reviewer2, "Standout post — proposing a quality bonus.", bonus: 3.00m);
        // Pending live check that is already due (48h after posting).
        var s6 = Plan(sara, eats, P.Instagram, Ago(2), postedBefore: TimeSpan.FromHours(3));
        s6.Then(S.Approve, Ago(1), Reviewer1);
        Plan(sara, aurora, P.Instagram, _now.AddHours(-20)); // waiting for review

        // Sara's resolved support ticket and the goodwill credit it led to.
        At(Ago(7), () => CreateSaraResolvedTicketAsync(s11));
        Adjust(sara, 2.50m, "USD", Ago(6),
            "Goodwill credit: Karachi Eats first-post bonus was not shown in the estimate because of a pricing delay.", () => _saraTicketReference);

        // ---------------------------------------------------------------- Sara's referrals
        var zainab = Person("zainab.malik");
        var z1 = Plan(zainab, eats, P.Instagram, Ago(34));
        z1.Then(S.Approve, Ago(33), Reviewer1).Then(S.LiveConfirm, z1.PostedAt.AddHours(54), Reviewer2);
        var z2 = Plan(zainab, nimbus, P.Instagram, Ago(30));
        z2.Then(S.Approve, Ago(29), Reviewer2);
        var usman = Person("usman.tariq"); // referral flagged: shared device with Zainab's registration
        var u1 = Plan(usman, nimbus, P.TikTok, Ago(12));
        u1.Then(S.Approve, Ago(11), Reviewer1);
        Plan(Person("karim.mostafa"), nimbus, P.Facebook, Ago(1)); // referral still pending

        // ---------------------------------------------------------------- Velocity: 11 posts in under 24 hours
        var bilal = Person("bilal.ahmed");
        var start = _now.Date.AddDays(-6).AddHours(5);
        var sequence = new (DemoCampaign Campaign, P Platform)[]
        {
            (nimbus, P.Instagram), (eats, P.Instagram), (nimbus, P.TikTok), (eats, P.Facebook), (nimbus, P.Facebook), (eats, P.TikTok),
            (nimbus, P.Instagram), (eats, P.Instagram), (nimbus, P.TikTok), (nimbus, P.Facebook), (eats, P.Facebook),
        };
        for (var i = 0; i < sequence.Length; i++)
        {
            var plan = Plan(bilal, sequence[i].Campaign, sequence[i].Platform, start.AddMinutes(i * 110), postedBefore: TimeSpan.FromMinutes(40));
            var decide = start.AddDays(1).AddMinutes(i * 25);
            var reviewer = i % 2 == 0 ? Reviewer1 : Reviewer2;
            switch (i)
            {
                case 6:
                    plan.Then(S.Reject, decide, reviewer, "Near-identical to your post from this morning — one post per format per day.");
                    break;
                case 7:
                    plan.Then(S.Reject, decide, reviewer, "Same photo as your earlier Karachi Eats post; please share a different moment.");
                    break;
                case 8:
                    plan.Then(S.RequestCorrection, decide, reviewer, "Please add the campaign hashtags to the caption and resubmit.");
                    break;
                case 10:
                    plan.Then(S.Reject, decide, reviewer, "Posting velocity: 11 posts in 24 hours, and this caption repeats an earlier post.");
                    break;
                default:
                    plan.Then(S.Approve, decide, reviewer);
                    if (sequence[i].Campaign == eats) plan.Then(S.LiveConfirm, plan.PostedAt.AddHours(50), OtherReviewer(reviewer));
                    break;
            }
        }

        // ---------------------------------------------------------------- Payout hold participant
        var aisha = Person("hold.participant");
        Plan(aisha, nimbus, P.Instagram, Ago(45)).Then(S.Approve, Ago(44), Reviewer1);
        Plan(aisha, nimbus, P.TikTok, Ago(38)).Then(S.Approve, Ago(37), Reviewer2);
        var a3 = Plan(aisha, bloom, P.Instagram, Ago(26));
        a3.Then(S.Approve, Ago(25), Reviewer1).Then(S.LiveConfirm, a3.PostedAt.AddHours(75), Reviewer2);
        Plan(aisha, nimbus, P.Instagram, Ago(16)).Then(S.Approve, Ago(15), Reviewer2);

        // ---------------------------------------------------------------- Suspended participant (copied screenshot)
        var emily = Person("emily.carter");
        var e1 = Plan(emily, nimbus, P.Instagram, c1.AddDays(1));
        e1.Then(S.Approve, e1.SubmittedAt.AddHours(6), Reviewer2, "Nice post — proposing a quality bonus.", bonus: 4.00m)
          .Then(S.BonusDecline, e1.SubmittedAt.AddHours(30), Manager,
              "Good post, but it doesn't meet the standout criteria (original footage and above-average engagement).");
        var jack = Person("suspended.participant");
        var j1 = Plan(jack, nimbus, P.Instagram, c1.AddDays(2));
        j1.Then(S.Approve, j1.SubmittedAt.AddHours(6), Reviewer1);
        var j2 = Plan(jack, nimbus, P.TikTok, c1.AddDays(4));
        j2.Then(S.Approve, j2.SubmittedAt.AddHours(5), Reviewer2);
        var j3 = Plan(jack, nimbus, P.Instagram, c1.AddDays(6), png: e1.Png);
        j3.Then(S.Reject, j3.SubmittedAt.AddHours(8), Reviewer1, "The screenshot is identical to another creator's submission.");

        // ---------------------------------------------------------------- Duplicate screenshot + upheld appeal
        var chloe = Person("chloe.evans");
        var ch = Plan(chloe, nimbus, P.TikTok, Ago(20));
        ch.Then(S.Approve, ch.SubmittedAt.AddHours(7), Reviewer1);
        var harry = Person("harry.patel");
        var ha = Plan(harry, nimbus, P.Instagram, Ago(19), png: ch.Png);
        ha.Then(S.Reject, ha.SubmittedAt.AddHours(6), Reviewer1, "The screenshot is identical to another participant's submission.")
          .Then(S.Appeal, Ago(18), harry, "I took this screenshot myself on my own phone — the post is mine, please check the link again.")
          .Then(S.AppealUphold, Ago(16), Reviewer2, "The uploaded file is byte-for-byte identical to another creator's upload; the decision stands.");

        // ---------------------------------------------------------------- Outside the campaign window
        var sofia = Person("sofia.ramirez");
        Plan(sofia, leaf, P.Instagram, leaf.Campaign.StartsAt.AddDays(1), postedBefore: TimeSpan.FromDays(3))
            .Then(S.Reject, leaf.Campaign.StartsAt.AddDays(1).AddHours(10), Reviewer2, "The post was published before the campaign started.");
        var oliver = Person("oliver.bennett");
        var ob = Plan(oliver, leaf, P.X, leaf.Campaign.EndsAt.AddDays(1).AddHours(2), postedBefore: TimeSpan.FromHours(3));
        ob.Then(S.Approve, ob.SubmittedAt.AddHours(20), Reviewer1,
            "Posted a few hours after the window closed; accepted under the grace period agreed with LedgerLeaf.");

        // ---------------------------------------------------------------- Appeals: open and overturned
        var kavya = Person("kavya.iyer");
        Plan(kavya, nimbus, P.Instagram, Ago(4))
            .Then(S.Reject, Ago(3), Reviewer1, "The post is missing the required paid-partnership disclosure.")
            .Then(S.Appeal, Ago(2), kavya, "Instagram shows the 'Paid partnership' label above my post and #ad is in the first line — please check again.");
        var ahmed = Person("ahmed.samir");
        Plan(ahmed, leaf, P.X, Ago(18))
            .Then(S.Reject, Ago(17), Reviewer2, "The post is missing the required paid-partnership disclosure.")
            .Then(S.Appeal, Ago(16), ahmed, "The disclosure '#ad Sponsored by LedgerLeaf' is at the end of my post; the screenshot cut it off.")
            .Then(S.AppealOverturn, Ago(14), Reviewer1, "Confirmed on the live post: the disclosure is present. Approved.");

        // ---------------------------------------------------------------- Clawback: paid, then reversed, netted next batch
        var hamza = Person("hamza.qureshi");
        var h1 = Plan(hamza, leaf, P.X, c3.AddDays(-8));
        h1.Then(S.Approve, h1.SubmittedAt.AddHours(10), Reviewer1)
          .Then(S.Reverse, PrepareTime(_p2).AddDays(-2), Reviewer2, "Most engagement on this post came from inauthentic accounts.");
        Plan(hamza, leaf, P.LinkedIn, c3.AddDays(-6)).Then(S.Approve, c3.AddDays(-6).AddHours(12), Reviewer2);
        Plan(hamza, leaf, P.LinkedIn, c2.AddDays(-7)).Then(S.Approve, c2.AddDays(-7).AddHours(8), Reviewer1);
        Plan(hamza, leaf, P.X, c2.AddDays(-6)).Then(S.Approve, c2.AddDays(-6).AddHours(8), Reviewer2);
        Plan(hamza, nimbus, P.X, c2.AddDays(-5)).Then(S.Approve, c2.AddDays(-5).AddHours(6), Reviewer1);

        // ---------------------------------------------------------------- Live check: post removed → reversal
        var omar = Person("omar.alsuwaidi");
        var om = Plan(omar, bloom, P.TikTok, Ago(15));
        om.Then(S.Approve, Ago(14), Reviewer1)
          .Then(S.LiveRemove, om.PostedAt.AddHours(77), Reviewer2, "The video now shows 'This post is unavailable' — removed by the author.");
    }

    /// <summary>
    /// Two pending submissions get claims (one live, one whose claim has expired) and a third is withdrawn by its participant.
    /// </summary>
    private void PlanReviewClaims()
    {
        var waiting = _plans.Where(p => p.Steps.Count == 0 && !p.Who.Scripted && p.SubmittedAt > _now.AddHours(-30))
            .OrderBy(p => p.SubmittedAt).ToList();
        if (waiting.Count > 0) waiting[0].Then(StepKind.Claim, _now.AddMinutes(-4), Reviewer2);
        if (waiting.Count > 1) waiting[1].Then(StepKind.Claim, _now.AddHours(-3), Reviewer1);
        // One participant withdraws a submission before review (wrong link), so the withdrawn state is in the demo too.
        if (waiting.Count > 2)
            waiting[2].Then(StepKind.Withdraw, _now.AddHours(-2), waiting[2].Who, "Posted the wrong link. I'll submit the right post.");
    }
}
