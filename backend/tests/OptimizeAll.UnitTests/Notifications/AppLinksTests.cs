using System.Reflection;
using System.Text.Json;
using OptimizeAll.Api.Common.Notifications;

namespace OptimizeAll.UnitTests.Notifications;

/// <summary>
/// Keeps <see cref="AppLinks"/> in sync with <c>frontend/src/app/appLinks.fixture.json</c>, whose paths the frontend
/// test <c>src/app/appLinks.test.ts</c> resolves against the real router.
/// </summary>
public sealed class AppLinksTests
{
    private static readonly Guid SampleId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private const string SampleSlug = "sample-campaign-slug";
    private const string SampleCode = "SAMPLEcode42";

    /// <summary>Every public AppLinks member rendered as a route pattern (:id, :slug, :code), keyed by member name.</summary>
    private static Dictionary<string, string> BackendPatterns()
    {
        var result = new Dictionary<string, string>();
        foreach (var field in typeof(AppLinks).GetFields(BindingFlags.Public | BindingFlags.Static))
            result[field.Name] = (string)field.GetValue(null)!;
        foreach (var method in typeof(AppLinks).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var args = method.GetParameters().Select(p => p.ParameterType == typeof(Guid)
                ? (object)SampleId
                : p.Name == "code" ? SampleCode : SampleSlug).ToArray();
            var path = (string)method.Invoke(null, args)!;
            result[method.Name] = path.Replace(SampleId.ToString(), ":id").Replace(SampleSlug, ":slug").Replace(SampleCode, ":code");
        }
        return result;
    }

    private static Dictionary<string, string> FixturePatterns()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(FixturePath(dir.FullName)))
            dir = dir.Parent;
        Assert.True(dir is not null, "frontend/src/app/appLinks.fixture.json was not found above the test directory.");
        using var json = JsonDocument.Parse(File.ReadAllText(FixturePath(dir!.FullName)));
        return json.RootElement.GetProperty("links").EnumerateArray()
            .ToDictionary(l => l.GetProperty("name").GetString()!, l => l.GetProperty("path").GetString()!);
    }

    private static string FixturePath(string root) => Path.Combine(root, "frontend", "src", "app", "appLinks.fixture.json");

    [Fact]
    public void Every_link_matches_the_frontend_fixture()
    {
        var backend = BackendPatterns();
        var fixture = FixturePatterns();
        Assert.Equal(fixture.OrderBy(kv => kv.Key), backend.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void Links_are_app_relative_single_slash_paths()
    {
        foreach (var (name, path) in BackendPatterns())
        {
            Assert.True(path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal), name);
            Assert.DoesNotContain("\\", path);
            Assert.DoesNotContain("?", path);
        }
    }

    [Fact]
    public void Enumerates_the_expected_outputs()
    {
        var id = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        Assert.Equal($"/app/submissions/{id}", AppLinks.Submission(id));
        Assert.Equal($"/app/payouts/{id}", AppLinks.Payout(id));
        Assert.Equal($"/app/support/{id}", AppLinks.SupportTicket(id));
        Assert.Equal($"/review/queue/{id}", AppLinks.ReviewSubmission(id));
        Assert.Equal($"/finance/batches/{id}", AppLinks.FinanceBatch(id));
        Assert.Equal("/app/campaigns/spring-sale", AppLinks.Campaign("spring-sale"));
        Assert.Equal("/c/spring-sale", AppLinks.PublicCampaign("spring-sale"));
        Assert.Equal("/join/Ab12Cd34", AppLinks.Invitation("Ab12Cd34"));
        Assert.Equal("/login", AppLinks.Login);
        Assert.Equal("/register", AppLinks.Register);
        Assert.Equal("/forgot-password", AppLinks.ForgotPassword);
        Assert.Equal("/verify-email", AppLinks.VerifyEmail);
        Assert.Equal("/reset-password", AppLinks.ResetPassword);
        Assert.Equal("/app", AppLinks.ParticipantHome);
        Assert.Equal("/app/campaigns", AppLinks.Campaigns);
        Assert.Equal("/app/earnings", AppLinks.Earnings);
        Assert.Equal("/app/payouts", AppLinks.Payouts);
        Assert.Equal("/app/profile", AppLinks.Profile);
        Assert.Equal("/app/profile/payout-details", AppLinks.PayoutDetails);
        Assert.Equal("/app/profile/notification-preferences", AppLinks.NotificationPreferences);
        Assert.Equal("/app/social-accounts", AppLinks.SocialAccounts);
        Assert.Equal("/app/achievements", AppLinks.Achievements);
        Assert.Equal("/app/referrals", AppLinks.Referrals);
        Assert.Equal("/review/live-checks", AppLinks.ReviewLiveChecks);
        // Parameters are escaped so they can never add path segments or a query.
        Assert.Equal("/app/campaigns/a%2Fb%3Fx", AppLinks.Campaign("a/b?x"));
    }
}
