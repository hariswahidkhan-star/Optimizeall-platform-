using System.Text.RegularExpressions;

namespace OptimizeAll.Api.Modules.Integrations;

public enum IntegrationCategory
{
    Social,
    Ads,
    Messaging,
    Email,
    Seo,
    Payments,
    Security,
}

/// <summary>A setting or secret the provider needs. Secrets are write-only (never returned by the API).</summary>
public sealed record ProviderField(string Key, string Label, bool Required, string? Help = null, string? Pattern = null, int MaxLength = 500, string? Placeholder = null);

/// <summary>Describes a third-party provider: what to collect, where to find it and whether it can be verified automatically.</summary>
public sealed record ProviderDescriptor(
    string Key, string Name, IntegrationCategory Category, string Description, string HelpText, string? DocsUrl,
    IReadOnlyList<ProviderField> Settings, IReadOnlyList<ProviderField> Secrets, bool AgencyWide, bool PerClient, bool SupportsVerification,
    bool TokensExpire);

/// <summary>
/// Registry of supported providers. Settings/secrets posted for a connection are validated against the descriptor
/// (required fields present, no unknown keys, patterns and lengths), so adapters can rely on the shape.
/// </summary>
public static class ProviderRegistry
{
    private static ProviderField S(string key, string label, bool required = true, string? help = null, string? pattern = null, string? placeholder = null, int max = 500) =>
        new(key, label, required, help, pattern, max, placeholder);

    private const string NumericId = @"^[0-9]{1,30}$";

    public static readonly IReadOnlyList<ProviderDescriptor> All = new ProviderDescriptor[]
    {
        new("meta", "Meta (Facebook & Instagram)", IntegrationCategory.Social, "Publish to Facebook Pages and Instagram business accounts and read insights.",
            "Create a Meta app, add the Pages and Instagram Graph API products, and generate a long-lived Page access token for the client's Page.",
            "https://developers.facebook.com/docs/pages-api",
            new[] { S("pageId", "Facebook Page ID", pattern: NumericId), S("instagramAccountId", "Instagram business account ID", false, pattern: NumericId) },
            new[] { S("pageAccessToken", "Page access token", help: "Long-lived tokens expire after about 60 days; set the expiry date to get a reminder.") },
            false, true, false, true),
        new("x", "X (Twitter)", IntegrationCategory.Social, "Publish posts and read engagement for an X account.",
            "Create a project in the X developer portal, enable OAuth 2.0 with tweet.write, and authorize the client's account.",
            "https://developer.x.com/en/docs/authentication/oauth-2-0",
            new[] { S("username", "Account handle", pattern: @"^@?[A-Za-z0-9_]{1,15}$", placeholder: "@brand") },
            new[] { S("accessToken", "OAuth 2.0 access token"), S("refreshToken", "Refresh token", false) }, false, true, false, true),
        new("linkedin", "LinkedIn", IntegrationCategory.Social, "Publish to LinkedIn company pages.",
            "Create a LinkedIn app with the Community Management API and authorize an admin of the company page.",
            "https://learn.microsoft.com/linkedin/marketing/",
            new[] { S("organizationId", "Organization ID", pattern: NumericId) },
            new[] { S("accessToken", "Access token", help: "LinkedIn tokens expire after 60 days.") }, false, true, false, true),
        new("tiktok", "TikTok", IntegrationCategory.Social, "Publish videos to a TikTok business account.",
            "Register a TikTok for Developers app with the Content Posting API and authorize the client's account.",
            "https://developers.tiktok.com/doc/content-posting-api-get-started",
            new[] { S("openId", "Open ID") }, new[] { S("accessToken", "Access token"), S("refreshToken", "Refresh token", false) }, false, true, false, true),
        new("youtube", "YouTube", IntegrationCategory.Social, "Upload videos and read channel analytics.",
            "Create an OAuth client in Google Cloud with the YouTube Data API v3 enabled and authorize the channel owner.",
            "https://developers.google.com/youtube/v3",
            new[] { S("channelId", "Channel ID", pattern: @"^UC[A-Za-z0-9_-]{22}$") },
            new[] { S("accessToken", "OAuth access token"), S("refreshToken", "Refresh token", false) }, false, true, false, true),
        new("pinterest", "Pinterest", IntegrationCategory.Social, "Create pins on a Pinterest business account.",
            "Create an app on Pinterest Developers and authorize the account with pins:write and boards:read.",
            "https://developers.pinterest.com/docs/api/v5/",
            new[] { S("boardId", "Default board ID", false) }, new[] { S("accessToken", "Access token") }, false, true, false, true),
        new("google-business", "Google Business Profile", IntegrationCategory.Seo, "Read reviews and publish updates for a Business Profile location.",
            "Enable the Business Profile APIs in Google Cloud (access requires Google approval) and authorize a profile manager.",
            "https://developers.google.com/my-business",
            new[] { S("accountId", "Account ID", pattern: NumericId), S("locationId", "Location ID", pattern: NumericId) },
            new[] { S("accessToken", "OAuth access token"), S("refreshToken", "Refresh token", false) }, false, true, false, true),
        new("google-ads", "Google Ads", IntegrationCategory.Ads, "Sync campaigns, spend and conversions from Google Ads.",
            "Apply for a developer token in your Google Ads manager account and authorize access to the client's customer ID.",
            "https://developers.google.com/google-ads/api/docs/start",
            new[] { S("customerId", "Customer ID", pattern: @"^\d{3}-?\d{3}-?\d{4}$", placeholder: "123-456-7890"), S("loginCustomerId", "Manager (MCC) customer ID", false, pattern: @"^\d{3}-?\d{3}-?\d{4}$") },
            new[] { S("developerToken", "Developer token"), S("refreshToken", "OAuth refresh token"), S("clientSecret", "OAuth client secret", false) },
            true, true, false, false),
        new("meta-ads", "Meta Ads", IntegrationCategory.Ads, "Sync Facebook and Instagram ad campaigns and spend.",
            "Use a system-user token from Meta Business Manager with ads_read (and ads_management to edit).",
            "https://developers.facebook.com/docs/marketing-apis",
            new[] { S("adAccountId", "Ad account ID", pattern: @"^(act_)?[0-9]{1,30}$", placeholder: "act_1234567890") },
            new[] { S("accessToken", "System-user access token") }, true, true, false, true),
        new("tiktok-ads", "TikTok Ads", IntegrationCategory.Ads, "Sync TikTok ad campaigns and spend.",
            "Create a TikTok for Business developer app and authorize the advertiser account.",
            "https://business-api.tiktok.com/portal/docs",
            new[] { S("advertiserId", "Advertiser ID", pattern: NumericId) }, new[] { S("accessToken", "Access token") }, true, true, false, false),
        new("linkedin-ads", "LinkedIn Ads", IntegrationCategory.Ads, "Sync LinkedIn Campaign Manager campaigns and spend.",
            "Request the Advertising API product for your LinkedIn app and authorize an ad account user.",
            "https://learn.microsoft.com/linkedin/marketing/integrations/ads/getting-started",
            new[] { S("adAccountId", "Ad account ID", pattern: NumericId) }, new[] { S("accessToken", "Access token") }, true, true, false, true),
        new("microsoft-ads", "Microsoft Advertising", IntegrationCategory.Ads, "Sync Microsoft (Bing) Ads campaigns and spend.",
            "Get a developer token from the Microsoft Advertising developer portal and authorize through Microsoft identity.",
            "https://learn.microsoft.com/advertising/guides/get-started",
            new[] { S("accountId", "Account ID", pattern: NumericId), S("customerId", "Customer ID", pattern: NumericId) },
            new[] { S("developerToken", "Developer token"), S("refreshToken", "OAuth refresh token") }, true, true, false, false),
        new("twilio", "Twilio SMS", IntegrationCategory.Messaging, "Send SMS campaigns and notifications.",
            "Copy the Account SID and Auth Token from the Twilio Console, and a sending number or Messaging Service SID.",
            "https://www.twilio.com/docs/sms",
            new[] { S("accountSid", "Account SID", pattern: @"^AC[0-9a-fA-F]{32}$"), S("fromNumber", "Sender (E.164 number or Messaging Service SID)", pattern: @"^(\+[1-9]\d{7,14}|MG[0-9a-fA-F]{32})$") },
            new[] { S("authToken", "Auth token") }, true, true, true, false),
        new("whatsapp-cloud", "WhatsApp Cloud API", IntegrationCategory.Messaging, "Send WhatsApp template messages through Meta's Cloud API.",
            "In Meta Business Manager add WhatsApp to your app, register a phone number and create a permanent system-user token.",
            "https://developers.facebook.com/docs/whatsapp/cloud-api",
            new[] { S("phoneNumberId", "Phone number ID", pattern: NumericId), S("businessAccountId", "WhatsApp business account ID", pattern: NumericId) },
            new[] { S("accessToken", "Permanent access token") }, true, true, false, false),
        new("sendgrid", "SendGrid", IntegrationCategory.Email, "Send email campaigns through Twilio SendGrid.",
            "Create an API key with Mail Send permission and verify your sender domain.",
            "https://docs.sendgrid.com/for-developers/sending-email/api-getting-started",
            new[]
            {
                S("fromEmail", "From address", pattern: @"^[^@\s]+@[^@\s]+\.[^@\s]+$"), S("fromName", "From name", false),
                // Read by the signed event webhook (bounces, spam reports → suppression list); without it the webhook answers 503.
                S("webhookPublicKey", "Event webhook verification key", false,
                    "Mail Settings → Signed Event Webhook: the public key (base64 or PEM). Bounces and spam reports are refused without it.",
                    @"^[A-Za-z0-9+/=\s-]+$", max: 1000),
            },
            new[] { S("apiKey", "API key", pattern: @"^SG\.[A-Za-z0-9_.-]{20,}$") }, true, true, true, false),
        new("mailgun", "Mailgun", IntegrationCategory.Email, "Send email campaigns through Mailgun.",
            "Add and verify a sending domain, then create a domain sending key or use the private API key.",
            "https://documentation.mailgun.com/docs/mailgun/api-reference/",
            new[] { S("domain", "Sending domain", pattern: @"^[a-z0-9.-]+\.[a-z]{2,}$"), S("region", "Region (us or eu)", false, pattern: "^(us|eu)$"), S("fromEmail", "From address", pattern: @"^[^@\s]+@[^@\s]+\.[^@\s]+$") },
            new[]
            {
                S("apiKey", "API key"),
                // Read by the webhook endpoint (bounces, complaints → suppression list); without it the webhook answers 503.
                S("webhookSigningKey", "HTTP webhook signing key", false,
                    "Sending → Webhooks → HTTP webhook signing key. Bounces and complaints are refused without it."),
            }, true, true, true, false),
        new("dataforseo", "DataForSEO", IntegrationCategory.Seo, "Daily rank tracking (Google SERP positions, SERP features, competitors).",
            "Create a DataForSEO account and copy the API login and password from the dashboard (API Access).",
            "https://docs.dataforseo.com/v3/serp/overview/",
            Array.Empty<ProviderField>(), new[] { S("login", "API login"), S("password", "API password") }, true, true, true, false),
        new("google-search-console", "Google Search Console", IntegrationCategory.Seo, "Import clicks, impressions, CTR and position by query and page.",
            "Create an OAuth client with the Search Console API (webmasters.readonly) and authorize a verified owner of the property.",
            "https://developers.google.com/webmaster-tools",
            new[] { S("propertyUrl", "Property", false, "e.g. sc-domain:example.com or https://www.example.com/ (defaults to the site URL).") },
            new[] { S("accessToken", "OAuth access token", help: "Access tokens last one hour; reconnect or refresh before imports."), S("refreshToken", "Refresh token", false) },
            false, true, true, true),
        new("stripe", "Stripe", IntegrationCategory.Payments, "Accept card payments for invoices.",
            "Copy a restricted or secret key from the Stripe Dashboard (Developers → API keys) and the webhook signing secret.",
            "https://docs.stripe.com/keys",
            new[] { S("publishableKey", "Publishable key", pattern: @"^pk_(test|live)_[A-Za-z0-9]+$") },
            new[] { S("secretKey", "Secret or restricted key", pattern: @"^(sk|rk)_(test|live)_[A-Za-z0-9]+$"), S("webhookSecret", "Webhook signing secret", false, pattern: @"^whsec_[A-Za-z0-9]+$") },
            true, false, true, false),
        new("paypal", "PayPal", IntegrationCategory.Payments, "Accept PayPal payments for invoices.",
            "Create a REST app in the PayPal Developer Dashboard and copy its client ID and secret.",
            "https://developer.paypal.com/api/rest/",
            new[] { S("clientId", "Client ID"), S("environment", "Environment (sandbox or live)", pattern: "^(sandbox|live)$") },
            new[] { S("clientSecret", "Client secret") }, true, false, false, false),
        new("hcaptcha", "hCaptcha", IntegrationCategory.Security, "Bot protection for landing-page and embedded forms.",
            "Add a site in the hCaptcha dashboard and copy the site key and the account secret.",
            "https://docs.hcaptcha.com/",
            new[] { S("siteKey", "Site key") }, new[] { S("secretKey", "Secret key") }, true, true, false, false),
        new("turnstile", "Cloudflare Turnstile", IntegrationCategory.Security, "Privacy-friendly bot protection for forms.",
            "Create a Turnstile widget in the Cloudflare dashboard and copy the site key and secret key.",
            "https://developers.cloudflare.com/turnstile/",
            new[] { S("siteKey", "Site key") }, new[] { S("secretKey", "Secret key") }, true, true, false, false),
    };

    private static readonly Dictionary<string, ProviderDescriptor> ByKey = All.ToDictionary(p => p.Key);

    public static ProviderDescriptor? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>Validates settings/secrets against the descriptor; returns field → errors (empty when valid).</summary>
    public static Dictionary<string, string[]> Validate(ProviderDescriptor d, IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> secrets, IReadOnlySet<string> savedSecretKeys)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var l)) errors[key] = l = new List<string>();
            l.Add(message);
        }

        foreach (var key in settings.Keys.Where(k => d.Settings.All(f => f.Key != k))) Add($"settings.{key}", "Unknown setting for this provider.");
        foreach (var key in secrets.Keys.Where(k => d.Secrets.All(f => f.Key != k))) Add($"secrets.{key}", "Unknown secret for this provider.");
        foreach (var f in d.Settings)
        {
            var has = settings.TryGetValue(f.Key, out var v) && !string.IsNullOrWhiteSpace(v);
            if (!has) { if (f.Required) Add($"settings.{f.Key}", $"{f.Label} is required."); continue; }
            Check(f, v!.Trim(), $"settings.{f.Key}", Add);
        }
        foreach (var f in d.Secrets)
        {
            var has = secrets.TryGetValue(f.Key, out var v) && !string.IsNullOrWhiteSpace(v);
            if (!has)
            {
                if (f.Required && !savedSecretKeys.Contains(f.Key)) Add($"secrets.{f.Key}", $"{f.Label} is required.");
                continue;
            }
            Check(f, v!.Trim(), $"secrets.{f.Key}", Add);
        }
        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static void Check(ProviderField f, string value, string key, Action<string, string> add)
    {
        if (value.Length > f.MaxLength) add(key, $"At most {f.MaxLength} characters.");
        else if (f.Pattern is not null && !Regex.IsMatch(value, f.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            add(key, $"{f.Label} does not look right — check the format.");
    }
}
