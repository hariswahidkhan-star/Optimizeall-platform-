using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptimizeAll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Criterion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Threshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Audience = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ActorType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BeforeJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AfterJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrelationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "campaign_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_categories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FriendlyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Xml = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BaseCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuoteCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.Id);
                    table.CheckConstraint("ck_exchange_rate_positive", "`Rate` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "faq_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Question = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Answer = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_items", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "homepage_banners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CtaLabel = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CtaUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Audience = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LanguageCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_homepage_banners", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "job_leases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Holder = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeasedUntil = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_leases", x => x.Name);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "job_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    JobName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RunKey = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Summary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Error = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstanceId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_runs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "onboarding_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionLabel = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionUrl = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletionRule = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_steps", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payout_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PeriodStart = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CutoffAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ScheduledPaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    FinalizedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CancelReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_batches", x => x.Id);
                    table.CheckConstraint("ck_payout_batch_total_nonnegative", "`TotalAmount` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payout_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Frequency = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnchorCutoffDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CutoffLocalTime = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentDelayDays = table.Column<int>(type: "int", nullable: false),
                    MinimumPayoutAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SettlementCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EarningHoldDays = table.Column<int>(type: "int", nullable: false),
                    AutoPrepareBatches = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ChangeReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_schedules", x => x.Id);
                    table.CheckConstraint("ck_payout_schedule_hold_nonnegative", "`EarningHoldDays` >= 0 AND `PaymentDelayDays` >= 0");
                    table.CheckConstraint("ck_payout_schedule_min_nonnegative", "`MinimumPayoutAmount` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "post_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hashtags = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LanguageCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_templates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Purpose = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StorageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValueJson = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_settings", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LanguageCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Interests = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailVerifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusChangedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Tier = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferralCode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppNumber = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppOptIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MarketingEmailOptIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailedLoginCount = table.Column<int>(type: "int", nullable: false),
                    LockoutEndsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastActiveAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SecurityVersion = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CategoryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Topics = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Visibility = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SubmissionDeadline = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostingInstructions = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultDisclosureText = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequiredHashtags = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequiredMentions = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BudgetCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxSubmissionsPerParticipant = table.Column<int>(type: "int", nullable: false),
                    MinPostLiveHours = table.Column<int>(type: "int", nullable: false),
                    RequireScreenshot = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    elig_min_account_age_days = table.Column<int>(type: "int", nullable: true),
                    elig_min_followers = table.Column<int>(type: "int", nullable: false),
                    elig_require_verified_account = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    elig_countries = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    elig_languages = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    elig_interests = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    elig_tiers = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LandingHeadline = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LandingBody = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeroImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TrackingDestinationUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "onboarding_step_completions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StepId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payout_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReleasedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ReleaseNote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payout_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EarningCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentProvider = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentReference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderTransactionId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    FailureReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HoldReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DestinationHint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payout_items", x => x.Id);
                    table.CheckConstraint("ck_payout_item_amount_positive", "`Amount` > 0");
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payout_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Method = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccountHolderName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaskedDestination = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EncryptedDestination = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreferredCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReferrerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ReferredUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CodeUsed = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QualifyingAction = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QualifyBy = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    QualifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RegistrationIpHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeviceHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FraudSignals = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RejectionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EarningEntryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_referrals", x => x.Id);
                    table.CheckConstraint("ck_referral_not_self", "`ReferrerUserId` <> `ReferredUserId`");
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TokenHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FamilyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RevokedReason = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReplacedByTokenId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedByIp = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserAgent = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "retention_message_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "social_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Handle = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedHandle = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccountCreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    FollowerCount = table.Column<int>(type: "int", nullable: false),
                    PrimaryLanguage = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AudienceCountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerificationStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VerificationNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reference = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Priority = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedToUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PayoutItemId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_achievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AchievementId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AwardedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Purpose = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TokenHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "campaign_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Body = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TemplateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "campaign_disclosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "campaign_platforms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "content_calendar_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduledFor = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hypothesis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Element = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    WinningVariantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invitation_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    UseCount = table.Column<int>(type: "int", nullable: false),
                    VisitCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "reward_rule_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DailyCapPerParticipant = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    WeeklyCapPerParticipant = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    CampaignCapPerParticipant = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ChangeReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DestinationUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmContent = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmTerm = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NotificationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderMessageId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "earning_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ReferralId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ExchangeRateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SettlementCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RewardRuleSetId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RewardRuleSetVersion = table.Column<int>(type: "int", nullable: true),
                    RewardRuleId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IdempotencyKey = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReversesEntryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ReversedByEntryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AvailableAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PayoutItemId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_earning_entries", x => x.Id);
                    table.CheckConstraint("ck_earning_rate_positive", "`ExchangeRate` > 0");
                    table.CheckConstraint("ck_earning_reason_required", "`Type` NOT IN ('Adjustment','Reversal') OR (`Reason` IS NOT NULL AND CHAR_LENGTH(`Reason`) > 0)");
                    table.CheckConstraint("ck_earning_reversal_negative", "`Type` <> 'Reversal' OR (`Amount` < 0 AND `ReversesEntryId` IS NOT NULL)");
                    table.CheckConstraint("ck_earning_sign_consistent", "(`Amount` >= 0 AND `SettlementAmount` >= 0) OR (`Amount` <= 0 AND `SettlementAmount` <= 0)");
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PayoutItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Provider = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderReference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "support_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TicketId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsInternalNote = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "experiment_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Instructions = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssetId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LandingHeadline = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LandingBody = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "reward_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RuleSetId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tier = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidFrom = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ValidTo = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovalMode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reward_rules", x => x.Id);
                    table.CheckConstraint("ck_reward_rules_amount_nonnegative", "`Amount` >= 0");
                    table.ForeignKey(
                        name: "FK_reward_rules_reward_rule_sets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "reward_rule_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SocialAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedPostUrl = table.Column<string>(type: "varchar(768)", maxLength: 768, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CaptionText = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScreenshotFileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScreenshotSha256 = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DecisionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrectionCount = table.Column<int>(type: "int", nullable: false),
                    RewardRuleSetId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RewardRuleSetVersion = table.Column<int>(type: "int", nullable: false),
                    EstimatedRewardAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RewardCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RiskScore = table.Column<int>(type: "int", nullable: false),
                    AssignedReviewerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ClaimedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ClaimExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LiveCheckStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LiveCheckDueAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LiveCheckedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LiveCheckedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ExperimentVariantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_clicks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TrackingLinkId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClickedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    VisitorHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Referrer = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsUnique = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSuspectedBot = table.Column<bool>(type: "tinyint(1)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "tracking_conversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TrackingLinkId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExternalReference = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "experiment_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VariantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubjectKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "appeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DecisionAppealed = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ResolutionNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "submission_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FromStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "submission_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ResolutionNote = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
