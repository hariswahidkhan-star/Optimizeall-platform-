using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Seed;

internal sealed record IndustrySeed(string Slug, string Name, string Icon, string Summary, string Body, string[] Challenges, string[] Services);

internal sealed record PageSeed(string Slug, string Title, string Summary, SitePageKind Kind, PageBlock[] Blocks, int Sort);

/// <summary>Baseline industries, CMS pages (incl. legal templates) and blog categories.</summary>
internal static class BaselinePages
{
    public static readonly IndustrySeed[] Industries =
    {
        new("ecommerce", "E-commerce & Retail", "shopping-bag", "Profitable growth for online stores — from first click to repeat purchase.",
            "Online retail margins are won or lost on acquisition cost and repeat purchase. We combine shopping ads, paid social, email and SMS, conversion optimisation and creator content into one plan, measured on contribution margin rather than revenue alone.",
            new[] { "Rising customer acquisition costs", "Low repeat purchase rates", "Checkout abandonment", "Dependence on one marketplace or channel" },
            new[] { "shopping-ecommerce-ads", "meta-ads", "email-marketing-automation", "ecommerce-growth", "influencer-ugc-marketing" }),
        new("saas", "SaaS & Technology", "cpu", "Pipeline and product-led growth for software companies.",
            "SaaS growth depends on efficient acquisition and strong activation. We build demand generation across search, LinkedIn and content, connect marketing to your CRM and product analytics, and focus on qualified pipeline, trial conversion and payback period.",
            new[] { "Long sales cycles", "High cost per demo", "Trials that never activate", "Attribution across many touchpoints" },
            new[] { "seo", "google-ads-ppc", "tiktok-linkedin-ads", "content-marketing-strategy", "marketing-automation-crm-setup" }),
        new("real-estate", "Real Estate", "building-2", "More qualified buyer, seller and tenant enquiries.",
            "Property decisions are local, visual and high-value. We help agencies, developers and property managers generate qualified enquiries with local SEO, paid search and social, virtual-tour video and CRM follow-up that responds in minutes.",
            new[] { "Portal fees and dependency", "Unqualified enquiries", "Slow lead follow-up", "Launching new developments quickly" },
            new[] { "local-seo-google-business-profile", "google-ads-ppc", "meta-ads", "video-production-youtube", "lead-generation" }),
        new("healthcare", "Healthcare & Wellness", "heart-pulse", "Compliant patient acquisition for clinics and wellness brands.",
            "Healthcare marketing must build trust while respecting strict advertising and privacy rules. We help clinics, practices and wellness brands attract patients with local SEO, reputation management, helpful content and compliant paid media — with privacy-conscious tracking.",
            new[] { "Advertising restrictions on health topics", "Patient privacy and data protection", "Reputation and reviews", "Competition from large groups" },
            new[] { "local-seo-google-business-profile", "online-reputation-management", "content-marketing-strategy", "google-ads-ppc", "analytics-tracking-setup" }),
        new("education", "Education & E-learning", "graduation-cap", "Enrolments for schools, universities and course creators.",
            "Enrolment journeys are long and involve parents, students and employers. We run always-on search and social campaigns around intake dates, create content that answers applicants' questions and nurture enquiries through to enrolment.",
            new[] { "Seasonal intake pressure", "Long decision journeys", "International student recruitment", "Proving marketing ROI per programme" },
            new[] { "seo", "google-ads-ppc", "social-media-advertising", "email-marketing-automation", "video-production-youtube" }),
        new("hospitality", "Hospitality & Travel", "utensils", "Direct bookings for hotels, restaurants and experiences.",
            "Every booking through an online travel agency costs commission. We grow direct bookings with metasearch and paid search, social content that sells the experience, reputation management and email campaigns that bring guests back.",
            new[] { "OTA commission costs", "Seasonality", "Review-driven decisions", "Visual storytelling at scale" },
            new[] { "local-seo-google-business-profile", "social-media-management", "online-reputation-management", "google-ads-ppc", "influencer-ugc-marketing" }),
        new("finance", "Finance & Fintech", "landmark", "Trust-first marketing for financial services and fintech.",
            "Financial services marketing is regulated and trust-sensitive. We build authority with expert content and digital PR, run compliant paid campaigns, and design onboarding journeys that convert applicants into active customers.",
            new[] { "Regulatory approval of ads and content", "Building trust with new audiences", "Complex products to explain", "High customer acquisition costs" },
            new[] { "content-marketing-strategy", "digital-pr-outreach", "google-ads-ppc", "landing-pages-cro", "email-marketing-automation" }),
        new("local-business", "Local Businesses", "store", "Get found, get chosen and get booked in your area.",
            "Local service businesses — trades, salons, clinics, gyms, restaurants — win on visibility and reviews. We make sure you appear in local search and maps, collect great reviews and run simple, effective local ads and social media.",
            new[] { "Competing with directories and big chains", "Few online reviews", "No time for marketing", "Unclear which ads actually work" },
            new[] { "local-seo-google-business-profile", "online-reputation-management", "social-media-management", "google-ads-ppc", "web-design-development" }),
        new("professional-services", "Professional Services", "briefcase", "Thought leadership and pipeline for firms that sell expertise.",
            "Law, accounting, consulting and B2B service firms grow through reputation and referrals — and digital can accelerate both. We position your experts, publish insight that ranks and gets shared, and turn website visitors into consultations.",
            new[] { "Differentiating from similar firms", "Partners short on time for content", "Long, relationship-driven sales", "Measuring marketing's effect on new clients" },
            new[] { "content-marketing-strategy", "seo", "digital-pr-outreach", "tiktok-linkedin-ads", "marketing-strategy-consulting" }),
    };

    public static readonly (string Slug, string Name, string Description)[] BlogCategories =
    {
        ("seo", "SEO", "Search engine optimisation guides, updates and case notes."),
        ("paid-media", "Paid media", "Google, Meta, TikTok and LinkedIn advertising."),
        ("social-media", "Social media", "Organic social, community and creator marketing."),
        ("content", "Content", "Content strategy, writing and video."),
        ("email", "Email & automation", "Email, SMS and lifecycle marketing."),
        ("analytics", "Analytics", "Measurement, tracking and reporting."),
        ("agency-news", "Agency news", "What's new at Optimize All."),
    };

    private static PageBlock Hero(string eyebrow, string title, string subtitle, SiteLink? primary = null, SiteLink? secondary = null) =>
        PageBlockValidator.Block(PageBlockTypes.Hero, new HeroBlock(eyebrow, title, subtitle, primary, secondary, null), "hero");

    private static PageBlock Text(string id, string markdown) => PageBlockValidator.Block(PageBlockTypes.RichText, new RichTextBlock(markdown), id);

    private static PageBlock Cta(string title, string text) => PageBlockValidator.Block(PageBlockTypes.Cta,
        new CtaBlock(title, text, new SiteLink("Get a free audit", "/free-audit"), new SiteLink("Book a call", "/book-a-consultation")), "cta");

    private const string LegalNotice =
        "> **Template — review with legal counsel before publishing.** This page was generated as a starting point for Optimize All. " +
        "It is not legal advice. Replace the bracketed placeholders, adapt it to the laws of every country you operate in, and have it reviewed by a qualified lawyer.\n\n";

    public static readonly PageSeed[] Pages =
    {
        new("about", "About us", "Optimize All is a full-service digital marketing agency built around measurable growth.", SitePageKind.Standard, new[]
        {
            Hero("About Optimize All", "Marketing that proves its worth", "We're a full-service digital marketing agency. One accountable team for search, social, paid media, content, email, brand and web — focused on the numbers that grow your business."),
            Text("story", "## Our story\n\nOptimize All started as a platform that pays everyday creators to share brands they believe in — with every post human-reviewed and clearly disclosed. Building that network taught us what brands really need from marketing: transparency, accountability and results you can verify.\n\nToday we bring the same principles to every service we offer. Whether you work with us on SEO, paid media or a new website, you'll always know what we're doing, why, and what it's delivering."),
            PageBlockValidator.Block(PageBlockTypes.FeaturesGrid, new FeaturesGridBlock("What we believe", null, new[]
            {
                new FeatureItem("Outcomes over outputs", "We measure success in leads, revenue and profit — not impressions or hours logged.", "target"),
                new FeatureItem("Radical transparency", "You see our work, our data and our reasoning in your client portal, any time.", "eye"),
                new FeatureItem("Honest measurement", "Estimates are labelled as estimates. We never dress up vanity metrics as results.", "shield-check"),
                new FeatureItem("Senior attention", "Strategists and specialists work on your account — not just account managers.", "users"),
            }), "values"),
            PageBlockValidator.Block(PageBlockTypes.ServicesGrid, new ServicesGridBlock("What we do", "Nine service lines, one integrated team.", null), "services"),
            PageBlockValidator.Block(PageBlockTypes.Testimonials, new TestimonialsBlock("What clients say", Array.Empty<Guid>()), "testimonials"),
            Cta("Let's grow together", "Tell us about your goals and we'll show you where the biggest opportunities are — free."),
        }, 10),
        new("how-we-work", "How we work", "Our process: audit, strategy, execution and transparent reporting.", SitePageKind.Standard, new[]
        {
            Hero("How we work", "A process built for accountability", "Every engagement follows the same four steps, so you always know what's happening and what it's achieving."),
            PageBlockValidator.Block(PageBlockTypes.FeaturesGrid, new FeaturesGridBlock("Our four-step process", null, new[]
            {
                new FeatureItem("1. Audit", "We start with an honest assessment of your marketing, tracking and competitors — and share it with you, whether or not you hire us.", "search"),
                new FeatureItem("2. Strategy", "A prioritised plan with targets, budgets and owners. You'll know what we'll do in the first 90 days before we start.", "compass"),
                new FeatureItem("3. Execution", "Specialists deliver the work. Drafts, creatives and changes are shared for approval in your client portal.", "rocket"),
                new FeatureItem("4. Reporting", "Live dashboards and a monthly review of results, learnings and next steps — in plain English.", "bar-chart-3"),
            }), "process"),
            Text("working", "## Working with us\n\n- **One point of contact.** Your account manager coordinates every specialist on your account.\n- **Client portal.** Approve deliverables, track projects, see reports and pay invoices in one place.\n- **No long lock-ins.** After an initial period, most plans run month to month.\n- **Your accounts, your data.** Ad accounts, analytics and content always belong to you."),
            PageBlockValidator.Block(PageBlockTypes.Faq, new FaqBlock("Questions about working with us", new[]
            {
                new FaqEntry("How quickly can we start?", "Most engagements start within two weeks of signing, beginning with onboarding and an audit."),
                new FaqEntry("How often will we hear from you?", "Weekly updates by default, a monthly results review, and your portal is always up to date."),
                new FaqEntry("Do you work with in-house teams?", "Yes. We often complement in-house marketers with specialist skills and extra capacity."),
            }), "faq"),
            Cta("Ready when you are", "Book a free consultation and we'll walk you through what the first 90 days would look like."),
        }, 20),
        new("pricing", "Pricing", "Transparent packages for every service, plus custom quotes for larger programmes.", SitePageKind.Standard, new[]
        {
            Text("intro", "Every service has clear starting packages. Prices exclude taxes and third-party costs such as advertising spend, software licences and stock media, which are billed separately and always at cost."),
            PageBlockValidator.Block(PageBlockTypes.Faq, new FaqBlock("Pricing questions", new[]
            {
                new FaqEntry("Is ad spend included?", "No. Advertising platforms bill ad spend directly to your account; our fees cover strategy, creative and management."),
                new FaqEntry("Can we combine services?", "Yes. Bundles of three or more services receive a discount — ask for a custom quote."),
                new FaqEntry("What's the minimum commitment?", "Monthly services have an initial three-month term, then continue month to month with 30 days' notice. Projects are priced per project."),
                new FaqEntry("Do you offer custom quotes?", "Yes. Larger or multi-market programmes are scoped individually after a consultation."),
            }), "faq"),
        }, 30),
        new("contact", "Contact", "Talk to Optimize All about your marketing.", SitePageKind.Standard, new[]
        {
            Text("intro", "Tell us a little about your business and what you'd like to achieve. A strategist — not a salesperson — will reply within one business day."),
        }, 40),
        new("privacy-policy", "Privacy policy", "How Optimize All collects, uses and protects personal data.", SitePageKind.Legal, new[]
        {
            Text("body", LegalNotice +
                "## Who we are\n\n[Legal entity name] (\"Optimize All\", \"we\") operates this website and the Optimize All platform. Contact our privacy team at [privacy@your-domain].\n\n" +
                "## What we collect\n\n- **Information you give us** — for example your name, email address, phone number, company and message when you complete a form, book a consultation, apply for a job or subscribe to our newsletter.\n- **Usage information** — pages visited and actions taken, collected with cookies and similar technologies **only where you have consented** (see our Cookie policy).\n- **Account information** — if you create an account on our platform, as described in the platform terms.\n\n" +
                "## Why we use it\n\n- To respond to enquiries and provide the services you request (contract / pre-contract steps).\n- To send our newsletter, only after you confirm your subscription (consent — you can withdraw at any time).\n- To assess job applications (consent / legitimate interest).\n- To measure and improve our website and marketing (consent).\n- To meet legal obligations and protect our rights.\n\n" +
                "## Sharing\n\nWe share personal data only with service providers who process it on our behalf (such as hosting, email delivery and analytics providers) under written agreements, and where required by law. We do not sell personal data.\n\n" +
                "## Retention\n\nWe keep enquiries for up to [24] months, job applications for up to [12] months, and newsletter data until you unsubscribe, unless the law requires longer.\n\n" +
                "## Your rights\n\nDepending on where you live you may have the right to access, correct, delete or port your data, to object to or restrict processing, and to withdraw consent. Contact [privacy@your-domain]. You may also complain to your data protection authority.\n\n" +
                "## International transfers\n\n[Describe transfer mechanisms, e.g. Standard Contractual Clauses.]\n\n" +
                "## Changes\n\nWe will post any changes on this page and update the date below.\n\n_Last updated: [date]_"),
        }, 100),
        new("terms-of-service", "Terms of service", "Terms that apply to using this website and our services.", SitePageKind.Legal, new[]
        {
            Text("body", LegalNotice +
                "## Agreement\n\nBy using this website you agree to these terms. Services we provide to clients are governed by the proposal and services agreement signed with each client, which prevails over these terms.\n\n" +
                "## Use of the website\n\nYou may browse and share our content for personal, non-commercial purposes. You must not misuse the website, attempt to gain unauthorised access, or submit false or malicious information through our forms.\n\n" +
                "## Intellectual property\n\nContent on this website — text, graphics, logos and code — belongs to [Legal entity name] or its licensors. Case studies are published with our clients' permission.\n\n" +
                "## No guarantees\n\nInformation on this website is general and not professional advice. Results described in case studies depend on each client's circumstances; figures marked as *estimated* are estimates, not measured outcomes.\n\n" +
                "## Liability\n\nTo the extent permitted by law, we are not liable for indirect or consequential losses arising from use of this website. [Adapt to applicable law.]\n\n" +
                "## Governing law\n\nThese terms are governed by the laws of [jurisdiction].\n\n_Last updated: [date]_"),
        }, 110),
        new("cookie-policy", "Cookie policy", "How and why this website uses cookies, and how to change your choices.", SitePageKind.Legal, new[]
        {
            Text("body", LegalNotice +
                "## What cookies are\n\nCookies are small files stored on your device. Similar technologies include local storage and pixels.\n\n" +
                "## Categories we use\n\n| Category | Purpose | Default |\n|---|---|---|\n| Necessary | Security, remembering your cookie choices, signing in | Always on |\n| Analytics | Understanding how visitors use the site (e.g. Google Analytics 4) | Off until you agree |\n| Marketing | Measuring and personalising advertising (e.g. Meta Pixel, Google Tag Manager tags) | Off until you agree |\n\n" +
                "Analytics and marketing tags are **not loaded at all** until you opt in through the cookie banner.\n\n" +
                "## Changing your choices\n\nUse the **Cookie settings** link in the footer at any time. Withdrawing consent stops new data collection; you can also delete cookies in your browser settings.\n\n" +
                "## Third parties\n\n[List providers, cookie names, lifetimes and links to their privacy policies.]\n\n_Last updated: [date]_"),
        }, 120),
        new("accessibility", "Accessibility statement", "Our commitment to an accessible website for everyone.", SitePageKind.Legal, new[]
        {
            Text("body", LegalNotice +
                "## Our commitment\n\nWe want everyone to be able to use this website. We design and test against the **Web Content Accessibility Guidelines (WCAG) 2.1 level AA**.\n\n" +
                "## What we do\n\n- Semantic structure with landmarks and a skip link\n- Keyboard access to every menu, form and dialog\n- Text alternatives for meaningful images\n- Colour contrast that meets AA, in light and dark themes\n- Carousels you can pause, and reduced motion when your device asks for it\n\n" +
                "## Known limitations\n\n[List any known issues and when you expect to fix them.]\n\n" +
                "## Feedback\n\nIf you find a barrier, email [accessibility@your-domain] and we'll respond within [5] business days.\n\n_Last reviewed: [date]_"),
        }, 130),
        new("refund-policy", "Refund policy", "How cancellations and refunds work for our services.", SitePageKind.Legal, new[]
        {
            Text("body", LegalNotice +
                "## Monthly services\n\nMonthly retainers can be cancelled with [30] days' written notice after any minimum term in your agreement. Fees for months already started are not refundable.\n\n" +
                "## Projects\n\nProject fees are invoiced in stages as set out in your proposal. If you cancel, you pay for work completed up to the cancellation date; prepaid amounts for work not started are refunded.\n\n" +
                "## Third-party costs\n\nAdvertising spend, software licences and other third-party costs are paid to or through those providers and follow their refund terms.\n\n" +
                "## Disputes\n\nIf you're unhappy, contact your account manager or [billing@your-domain]. We'll respond within [5] business days and work with you to resolve the issue.\n\n_Last updated: [date]_"),
        }, 140),
    };
}
