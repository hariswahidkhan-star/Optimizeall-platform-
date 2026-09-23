using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>
/// Canonical demo clients and users created by the "Demo" seed profile (<c>DeliveryDemoSeeder</c>, Order 200). Other
/// modules' demo seeders look these up by slug/email and — if missing — create them with exactly these values.
/// STAGING / DEMO DATA ONLY. Every demo account's password is <c>Demo#2026!pass</c>.
/// </summary>
public static class DeliveryDemoData
{
    public const string Domain = "demo.optimizeall.app";
    public const string Password = "Demo#2026!pass";

    public sealed record DemoClient(string Slug, string Name, string Industry, string CountryCode, string Currency, string TimeZone,
        ClientAccountStatus Status, string Website, string Summary);

    public static readonly DemoClient Nimbus = new("nimbus-fitness", "Nimbus Fitness", "SaaS fitness app", "US", "USD", "America/New_York",
        ClientAccountStatus.Active, "https://nimbusfitness.example", "Subscription fitness app with guided workouts and nutrition plans.");

    public static readonly DemoClient Wanderly = new("wanderly-travel", "Wanderly Travel", "Travel", "GB", "GBP", "Europe/London",
        ClientAccountStatus.Active, "https://wanderly.example", "Boutique tour operator for small-group trips across Europe and Asia.");

    public static readonly DemoClient Aurora = new("aurora-skincare", "Aurora Skincare", "E-commerce beauty", "AE", "AED", "Asia/Dubai",
        ClientAccountStatus.Active, "https://auroraskin.example", "Clean skincare brand selling direct-to-consumer across the GCC.");

    public static readonly DemoClient KarachiEats = new("karachi-eats", "Karachi Eats", "Restaurant group", "PK", "PKR", "Asia/Karachi",
        ClientAccountStatus.Onboarding, "https://karachieats.example", "Restaurant group with five dine-in locations and delivery.");

    public static readonly IReadOnlyList<DemoClient> Clients = new[] { Nimbus, Wanderly, Aurora, KarachiEats };

    public sealed record DemoStaff(string Email, string DisplayName, Role Role);

    public static readonly DemoStaff AccountManager = new($"am@{Domain}", "Amira Haddad", Role.AccountManager);
    public static readonly DemoStaff Strategist = new($"strategist@{Domain}", "Daniel Okafor", Role.Strategist);
    public static readonly DemoStaff Content = new($"content@{Domain}", "Priya Nair", Role.ContentCreator);
    public static readonly DemoStaff Designer = new($"designer@{Domain}", "Lucas Moreau", Role.Designer);
    public static readonly DemoStaff Seo = new($"seo@{Domain}", "Hina Qureshi", Role.SeoSpecialist);
    public static readonly DemoStaff Ads = new($"ads@{Domain}", "Marcus Chen", Role.AdsSpecialist);
    public static readonly DemoStaff Social = new($"social@{Domain}", "Sofia Alvarez", Role.SocialMediaManager);
    public static readonly DemoStaff Sales = new($"sales@{Domain}", "Omar Farooq", Role.SalesRep);

    public static readonly IReadOnlyList<DemoStaff> Staff = new[] { AccountManager, Strategist, Content, Designer, Seo, Ads, Social, Sales };

    public sealed record DemoClientUser(string Email, string DisplayName, string ClientSlug, ClientMemberRole Duty);

    public static readonly DemoClientUser NimbusOwner = new("owner@nimbus.demo.optimizeall.app", "Jordan Blake", Nimbus.Slug, ClientMemberRole.Owner);
    public static readonly DemoClientUser NimbusApprover = new("approver@nimbus.demo.optimizeall.app", "Taylor Reed", Nimbus.Slug, ClientMemberRole.Approver);
    public static readonly DemoClientUser NimbusBilling = new("billing@nimbus.demo.optimizeall.app", "Morgan Lee", Nimbus.Slug, ClientMemberRole.Billing);
    public static readonly DemoClientUser AuroraOwner = new("owner@aurora.demo.optimizeall.app", "Layla Al Mansoori", Aurora.Slug, ClientMemberRole.Owner);

    public static readonly IReadOnlyList<DemoClientUser> ClientUsers = new[] { NimbusOwner, NimbusApprover, NimbusBilling, AuroraOwner };
}
