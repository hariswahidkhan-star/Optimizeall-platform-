using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptimizeAll.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Criterion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Threshold = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PublishAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campaign_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    QuoteCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.Id);
                    table.CheckConstraint("ck_exchange_rate_positive", "CAST(\"Rate\" AS REAL) > 0");
                });

            migrationBuilder.CreateTable(
                name: "faq_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "homepage_banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CtaLabel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CtaUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_homepage_banners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_leases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Holder = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LeasedUntil = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_leases", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ActionLabel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ActionUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CompletionRule = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payout_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CutoffAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ScheduledPaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ExclusionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    InstructionsExportedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_batches", x => x.Id);
                    table.CheckConstraint("ck_payout_batch_total_nonnegative", "CAST(\"TotalAmount\" AS REAL) >= 0");
                });

            migrationBuilder.CreateTable(
                name: "payout_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AnchorCutoffDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CutoffLocalTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PaymentDelayDays = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumPayoutAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SettlementCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    EarningHoldDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoPrepareBatches = table.Column<bool>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChangeReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_schedules", x => x.Id);
                    table.CheckConstraint("ck_payout_schedule_hold_nonnegative", "\"EarningHoldDays\" >= 0 AND \"PaymentDelayDays\" >= 0");
                    table.CheckConstraint("ck_payout_schedule_min_nonnegative", "CAST(\"MinimumPayoutAmount\" AS REAL) >= 0");
                });

            migrationBuilder.CreateTable(
                name: "post_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Hashtags = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: false),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Interests = table.Column<string>(type: "TEXT", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Tier = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ReferralCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    WhatsAppNumber = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    WhatsAppOptIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarketingEmailOptIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastActiveAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SecurityVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Topics = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Visibility = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EndsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SubmissionDeadline = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PostingInstructions = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultDisclosureText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RequiredHashtags = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RequiredMentions = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    BudgetCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    MaxSubmissionsPerParticipant = table.Column<int>(type: "INTEGER", nullable: false),
                    MinPostLiveHours = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireScreenshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    elig_min_account_age_days = table.Column<int>(type: "INTEGER", nullable: true),
                    elig_min_followers = table.Column<int>(type: "INTEGER", nullable: false),
                    elig_require_verified_account = table.Column<bool>(type: "INTEGER", nullable: false),
                    elig_countries = table.Column<string>(type: "TEXT", nullable: false),
                    elig_languages = table.Column<string>(type: "TEXT", nullable: false),
                    elig_interests = table.Column<string>(type: "TEXT", nullable: false),
                    elig_tiers = table.Column<string>(type: "TEXT", nullable: false),
                    LandingHeadline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LandingBody = table.Column<string>(type: "TEXT", nullable: true),
                    HeroImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TrackingDestinationUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_campaigns_campaign_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "campaign_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_campaigns_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "client_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AccountManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LogoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BillingEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    BillingAddress = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TaxId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CrmCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_accounts_users_AccountManagerUserId",
                        column: x => x.AccountManagerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => new { x.UserId, x.Type, x.Channel });
                    table.ForeignKey(
                        name: "FK_notification_preferences_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    LinkUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "onboarding_step_completions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_step_completions", x => new { x.UserId, x.StepId });
                    table.ForeignKey(
                        name: "FK_onboarding_step_completions_onboarding_steps_StepId",
                        column: x => x.StepId,
                        principalTable: "onboarding_steps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_onboarding_step_completions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payout_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleaseNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_holds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payout_holds_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    EarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PaymentProvider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PaymentReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    HoldReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DestinationHint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_items", x => x.Id);
                    table.CheckConstraint("ck_payout_item_amount_positive", "CAST(\"Amount\" AS REAL) > 0");
                    table.ForeignKey(
                        name: "FK_payout_items_payout_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "payout_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payout_items_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AccountHolderName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    MaskedDestination = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EncryptedDestination = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    PreferredCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payout_profiles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferrerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferredUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeUsed = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    QualifyingAction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    QualifyBy = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    QualifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RegistrationIpHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    DeviceHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    FraudSignals = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EarningEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.Id);
                    table.CheckConstraint("ck_referral_not_self", "\"ReferrerUserId\" <> \"ReferredUserId\"");
                    table.ForeignKey(
                        name: "FK_referrals_users_ReferredUserId",
                        column: x => x.ReferredUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_referrals_users_ReferrerUserId",
                        column: x => x.ReferrerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    FamilyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RevokedReason = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByIp = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retention_message_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    DedupKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_message_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_retention_message_logs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "social_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedHandle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProfileUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AccountCreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    FollowerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PrimaryLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    AudienceCountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_social_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_social_accounts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayoutItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_tickets_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_achievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AchievementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AwardedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_achievements", x => new { x.UserId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_user_achievements_achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_achievements_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.UserId, x.Role });
                    table.ForeignKey(
                        name: "FK_user_roles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_campaign_assets_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_campaign_assets_post_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "post_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_campaign_assets_stored_files_FileId",
                        column: x => x.FileId,
                        principalTable: "stored_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "campaign_disclosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_disclosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_campaign_disclosures_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "campaign_platforms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_platforms", x => new { x.CampaignId, x.Platform });
                    table.ForeignKey(
                        name: "FK_campaign_platforms_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_calendar_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ScheduledFor = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_calendar_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_calendar_entries_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_calendar_entries_post_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "post_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Hypothesis = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Element = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    WinningVariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_experiments_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitation_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    MaxUses = table.Column<int>(type: "INTEGER", nullable: true),
                    UseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    VisitCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitation_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invitation_links_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_rule_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    DailyCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    WeeklyCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    CampaignCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_rule_sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reward_rule_sets_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tracking_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DestinationUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracking_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tracking_links_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_members",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_members", x => new { x.ClientAccountId, x.UserId });
                    table.ForeignKey(
                        name: "FK_client_members_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_deliveries_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "earning_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReferralId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    ExchangeRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SettlementAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    SettlementCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    RewardRuleSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RewardRuleSetVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RewardRuleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReversesEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedByEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AvailableAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayoutItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_earning_entries", x => x.Id);
                    table.CheckConstraint("ck_earning_rate_positive", "CAST(\"ExchangeRate\" AS REAL) > 0");
                    table.CheckConstraint("ck_earning_reason_required", "\"Type\" NOT IN ('Adjustment','Reversal') OR (\"Reason\" IS NOT NULL AND LENGTH(\"Reason\") > 0)");
                    table.CheckConstraint("ck_earning_reversal_negative", "\"Type\" <> 'Reversal' OR (CAST(\"Amount\" AS REAL) < 0 AND \"ReversesEntryId\" IS NOT NULL)");
                    table.CheckConstraint("ck_earning_sign_consistent", "(CAST(\"Amount\" AS REAL) >= 0 AND CAST(\"SettlementAmount\" AS REAL) >= 0) OR (CAST(\"Amount\" AS REAL) <= 0 AND CAST(\"SettlementAmount\" AS REAL) <= 0)");
                    table.ForeignKey(
                        name: "FK_earning_entries_earning_entries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "earning_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_earning_entries_exchange_rates_ExchangeRateId",
                        column: x => x.ExchangeRateId,
                        principalTable: "exchange_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_earning_entries_payout_items_PayoutItemId",
                        column: x => x.PayoutItemId,
                        principalTable: "payout_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_earning_entries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayoutItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ProviderReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_attempts_payout_items_PayoutItemId",
                        column: x => x.PayoutItemId,
                        principalTable: "payout_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_item_earnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayoutItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EarningEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SettlementAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_item_earnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payout_item_earnings_payout_items_PayoutItemId",
                        column: x => x.PayoutItemId,
                        principalTable: "payout_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TicketId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IsInternalNote = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_support_messages_support_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "support_tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experiment_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Instructions = table.Column<string>(type: "TEXT", nullable: true),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LandingHeadline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LandingBody = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiment_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_experiment_variants_experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reward_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    Tier = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ValidTo = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovalMode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_rules", x => x.Id);
                    table.CheckConstraint("ck_reward_rules_amount_nonnegative", "CAST(\"Amount\" AS REAL) >= 0");
                    table.ForeignKey(
                        name: "FK_reward_rules_reward_rule_sets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "reward_rule_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PostUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    NormalizedPostUrl = table.Column<string>(type: "TEXT", maxLength: 768, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CaptionText = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    ScreenshotFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScreenshotSha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CorrectionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RewardRuleSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RewardRuleSetVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedRewardAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    RewardCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    RiskScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedReviewerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LiveCheckStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LiveCheckDueAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LiveCheckedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LiveCheckedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExperimentVariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submissions_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_reward_rule_sets_RewardRuleSetId",
                        column: x => x.RewardRuleSetId,
                        principalTable: "reward_rule_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_social_accounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "social_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_stored_files_ScreenshotFileId",
                        column: x => x.ScreenshotFileId,
                        principalTable: "stored_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tracking_clicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackingLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    VisitorHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: true),
                    Referrer = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsUnique = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSuspectedBot = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracking_clicks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tracking_clicks_tracking_links_TrackingLinkId",
                        column: x => x.TrackingLinkId,
                        principalTable: "tracking_links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tracking_conversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackingLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalReference = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracking_conversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tracking_conversions_tracking_links_TrackingLinkId",
                        column: x => x.TrackingLinkId,
                        principalTable: "tracking_links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "experiment_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_experiment_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_experiment_assignments_experiment_variants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "experiment_variants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_experiment_assignments_experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "appeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DecisionAppealed = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolutionNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appeals_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_appeals_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_events_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolutionNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_flags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_flags_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievements_Key",
                table: "achievements",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_announcements_IsActive_PublishAt",
                table: "announcements",
                columns: new[] { "IsActive", "PublishAt" });

            migrationBuilder.CreateIndex(
                name: "IX_appeals_Status_CreatedAt",
                table: "appeals",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_appeals_SubmissionId",
                table: "appeals",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_appeals_UserId",
                table: "appeals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_Action",
                table: "audit_logs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_ActorUserId",
                table: "audit_logs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CreatedAt",
                table: "audit_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_EntityType_EntityId",
                table: "audit_logs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_campaign_assets_CampaignId_SortOrder",
                table: "campaign_assets",
                columns: new[] { "CampaignId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_campaign_assets_FileId",
                table: "campaign_assets",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_assets_TemplateId",
                table: "campaign_assets",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_categories_Slug",
                table: "campaign_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaign_disclosures_CampaignId_Platform_CountryCode",
                table: "campaign_disclosures",
                columns: new[] { "CampaignId", "Platform", "CountryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_CategoryId",
                table: "campaigns",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_CreatedByUserId",
                table: "campaigns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_Slug",
                table: "campaigns",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_Status_StartsAt_EndsAt",
                table: "campaigns",
                columns: new[] { "Status", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_client_accounts_AccountManagerUserId",
                table: "client_accounts",
                column: "AccountManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_client_accounts_Slug",
                table: "client_accounts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_accounts_Status",
                table: "client_accounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_client_members_UserId",
                table: "client_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_calendar_entries_CampaignId",
                table: "content_calendar_entries",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_content_calendar_entries_ScheduledFor",
                table: "content_calendar_entries",
                column: "ScheduledFor");

            migrationBuilder.CreateIndex(
                name: "IX_content_calendar_entries_TemplateId",
                table: "content_calendar_entries",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_CampaignId_Status",
                table: "earning_entries",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_ExchangeRateId",
                table: "earning_entries",
                column: "ExchangeRateId");

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_IdempotencyKey",
                table: "earning_entries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_PayoutItemId",
                table: "earning_entries",
                column: "PayoutItemId");

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_ReversesEntryId",
                table: "earning_entries",
                column: "ReversesEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_Status_AvailableAt",
                table: "earning_entries",
                columns: new[] { "Status", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_SubmissionId",
                table: "earning_entries",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_UserId_CampaignId_CreatedAt",
                table: "earning_entries",
                columns: new[] { "UserId", "CampaignId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_UserId_Status",
                table: "earning_entries",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rates_BaseCurrency_QuoteCurrency_EffectiveAt",
                table: "exchange_rates",
                columns: new[] { "BaseCurrency", "QuoteCurrency", "EffectiveAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_experiment_assignments_ExperimentId_SubjectKey",
                table: "experiment_assignments",
                columns: new[] { "ExperimentId", "SubjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_experiment_assignments_VariantId",
                table: "experiment_assignments",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_experiment_variants_ExperimentId_Key",
                table: "experiment_variants",
                columns: new[] { "ExperimentId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_experiments_CampaignId_Status",
                table: "experiments",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_homepage_banners_IsActive_SortOrder",
                table: "homepage_banners",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_invitation_links_CampaignId",
                table: "invitation_links",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_invitation_links_Code",
                table: "invitation_links",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_JobName_RunKey_Attempt",
                table: "job_runs",
                columns: new[] { "JobName", "RunKey", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_Status_StartedAt",
                table: "job_runs",
                columns: new[] { "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_NotificationId_Channel",
                table: "notification_deliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_deliveries_Status_NextAttemptAt",
                table: "notification_deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_ReadAt_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_step_completions_StepId",
                table: "onboarding_step_completions",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_steps_Key",
                table: "onboarding_steps",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_IdempotencyKey",
                table: "payment_attempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_attempts_PayoutItemId",
                table: "payment_attempts",
                column: "PayoutItemId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_batches_IdempotencyKey",
                table: "payout_batches",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payout_batches_PeriodKey_Currency",
                table: "payout_batches",
                columns: new[] { "PeriodKey", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_batches_Reference",
                table: "payout_batches",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payout_batches_Status_CutoffAt",
                table: "payout_batches",
                columns: new[] { "Status", "CutoffAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_holds_UserId_ReleasedAt",
                table: "payout_holds",
                columns: new[] { "UserId", "ReleasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_item_earnings_EarningEntryId",
                table: "payout_item_earnings",
                column: "EarningEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_item_earnings_PayoutItemId_EarningEntryId",
                table: "payout_item_earnings",
                columns: new[] { "PayoutItemId", "EarningEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payout_item_earnings_UserId",
                table: "payout_item_earnings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_BatchId_UserId",
                table: "payout_items",
                columns: new[] { "BatchId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payout_items_UserId_Status",
                table: "payout_items",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_payout_profiles_UserId",
                table: "payout_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payout_schedules_EffectiveFrom",
                table: "payout_schedules",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_referrals_ReferredUserId",
                table: "referrals",
                column: "ReferredUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_referrals_ReferrerUserId_Status",
                table: "referrals",
                columns: new[] { "ReferrerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_FamilyId",
                table: "refresh_tokens",
                columns: new[] { "UserId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_retention_message_logs_UserId_Kind_DedupKey",
                table: "retention_message_logs",
                columns: new[] { "UserId", "Kind", "DedupKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reward_rule_sets_CampaignId_Version",
                table: "reward_rule_sets",
                columns: new[] { "CampaignId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reward_rules_RuleSetId",
                table: "reward_rules",
                column: "RuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_social_accounts_Platform_NormalizedHandle",
                table: "social_accounts",
                columns: new[] { "Platform", "NormalizedHandle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_social_accounts_UserId",
                table: "social_accounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_social_accounts_VerificationStatus",
                table: "social_accounts",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_OwnerUserId",
                table: "stored_files",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_Sha256",
                table: "stored_files",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_StorageKey",
                table: "stored_files",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_events_SubmissionId_CreatedAt",
                table: "submission_events",
                columns: new[] { "SubmissionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_flags_SubmissionId_Type",
                table: "submission_flags",
                columns: new[] { "SubmissionId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_AssignedReviewerId",
                table: "submissions",
                column: "AssignedReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_CampaignId_Status",
                table: "submissions",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ContentHash",
                table: "submissions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_LiveCheckStatus_LiveCheckDueAt",
                table: "submissions",
                columns: new[] { "LiveCheckStatus", "LiveCheckDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_NormalizedPostUrl",
                table: "submissions",
                column: "NormalizedPostUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_RewardRuleSetId",
                table: "submissions",
                column: "RewardRuleSetId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ScreenshotFileId",
                table: "submissions",
                column: "ScreenshotFileId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ScreenshotSha256",
                table: "submissions",
                column: "ScreenshotSha256");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_SocialAccountId",
                table: "submissions",
                column: "SocialAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_Status_SubmittedAt",
                table: "submissions",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_UserId_CampaignId",
                table: "submissions",
                columns: new[] { "UserId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_support_messages_TicketId_CreatedAt",
                table: "support_messages",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Reference",
                table: "support_tickets",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_Status_Priority_UpdatedAt",
                table: "support_tickets",
                columns: new[] { "Status", "Priority", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_support_tickets_UserId",
                table: "support_tickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_tracking_clicks_TrackingLinkId_ClickedAt",
                table: "tracking_clicks",
                columns: new[] { "TrackingLinkId", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tracking_clicks_TrackingLinkId_VisitorHash",
                table: "tracking_clicks",
                columns: new[] { "TrackingLinkId", "VisitorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_tracking_conversions_TrackingLinkId_ExternalReference",
                table: "tracking_conversions",
                columns: new[] { "TrackingLinkId", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracking_links_CampaignId_UserId",
                table: "tracking_links",
                columns: new[] { "CampaignId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracking_links_Code",
                table: "tracking_links",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_achievements_AchievementId",
                table: "user_achievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_Role",
                table: "user_roles",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_user_tokens_TokenHash",
                table: "user_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_tokens_UserId_Purpose",
                table: "user_tokens",
                columns: new[] { "UserId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_users_CreatedAt",
                table: "users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedEmail",
                table: "users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_ReferralCode",
                table: "users",
                column: "ReferralCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Status",
                table: "users",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "appeals");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "campaign_assets");

            migrationBuilder.DropTable(
                name: "campaign_disclosures");

            migrationBuilder.DropTable(
                name: "campaign_platforms");

            migrationBuilder.DropTable(
                name: "client_members");

            migrationBuilder.DropTable(
                name: "content_calendar_entries");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "earning_entries");

            migrationBuilder.DropTable(
                name: "experiment_assignments");

            migrationBuilder.DropTable(
                name: "faq_items");

            migrationBuilder.DropTable(
                name: "homepage_banners");

            migrationBuilder.DropTable(
                name: "invitation_links");

            migrationBuilder.DropTable(
                name: "job_leases");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "onboarding_step_completions");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropTable(
                name: "payout_holds");

            migrationBuilder.DropTable(
                name: "payout_item_earnings");

            migrationBuilder.DropTable(
                name: "payout_profiles");

            migrationBuilder.DropTable(
                name: "payout_schedules");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "retention_message_logs");

            migrationBuilder.DropTable(
                name: "reward_rules");

            migrationBuilder.DropTable(
                name: "submission_events");

            migrationBuilder.DropTable(
                name: "submission_flags");

            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "tracking_clicks");

            migrationBuilder.DropTable(
                name: "tracking_conversions");

            migrationBuilder.DropTable(
                name: "user_achievements");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "client_accounts");

            migrationBuilder.DropTable(
                name: "post_templates");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "experiment_variants");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "onboarding_steps");

            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropTable(
                name: "tracking_links");

            migrationBuilder.DropTable(
                name: "achievements");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "payout_batches");

            migrationBuilder.DropTable(
                name: "reward_rule_sets");

            migrationBuilder.DropTable(
                name: "social_accounts");

            migrationBuilder.DropTable(
                name: "stored_files");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "campaign_categories");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
