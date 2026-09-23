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
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
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
                name: "billing_number_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastValue = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_number_sequences", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "brief_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceLine = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fields = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_templates", x => x.Id);
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
                name: "crm_assignment_cursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Pool = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_assignment_cursors", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_inbound_events",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DealId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AssignedUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ProcessedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_inbound_events", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_pipeline_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    WinProbability = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_pipeline_stages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_scoring_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Field = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MatchValue = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Points = table.Column<int>(type: "int", nullable: false),
                    MaxOccurrences = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_scoring_rules", x => x.Id);
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
                name: "delivery_dispatch_keys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_dispatch_keys", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_tracked_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SourceKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UrlHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_tracked_links", x => x.Id);
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
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faq_items", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "form_email_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ToAddress = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(10000)", maxLength: 10000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LastError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_email_outbox", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "form_templates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SchemaJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmitLabel = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SuccessMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoresponderSubject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoresponderBody = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_templates", x => x.Key);
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
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_homepage_banners", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "hourly_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hourly_rates", x => x.Id);
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
                name: "landing_page_templates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetaTitle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetaDescription = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlocksJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FormTemplateKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_templates", x => x.Key);
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
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
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
                    PeriodKey = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
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
                    ExclusionsJson = table.Column<string>(type: "json", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstructionsExportedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
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
                name: "project_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProjectType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceLines = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultBudgetHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    DurationDays = table.Column<int>(type: "int", nullable: true),
                    Milestones = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tasks = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Recurring = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_templates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sections = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_templates", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_audit_rules",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhyItMatters = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HowToFix = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_audit_rules", x => x.Key);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_citation_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Countries = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_citation_sources", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_awareness_days",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Countries = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_awareness_days", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_network_presets",
                columns: table => new
                {
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxTextLength = table.Column<int>(type: "int", nullable: false),
                    MaxTitleLength = table.Column<int>(type: "int", nullable: true),
                    MaxHashtags = table.Column<int>(type: "int", nullable: false),
                    RecommendedHashtags = table.Column<int>(type: "int", nullable: true),
                    MaxMentions = table.Column<int>(type: "int", nullable: false),
                    MaxMedia = table.Column<int>(type: "int", nullable: false),
                    MaxVideos = table.Column<int>(type: "int", nullable: false),
                    RequiresMedia = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresVideo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowsMixedMedia = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinAspectRatio = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    MaxAspectRatio = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    MaxVideoSeconds = table.Column<int>(type: "int", nullable: true),
                    MinVideoSeconds = table.Column<int>(type: "int", nullable: true),
                    MaxImageBytes = table.Column<long>(type: "bigint", nullable: true),
                    MaxAltTextLength = table.Column<int>(type: "int", nullable: false),
                    SupportsFirstComment = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LinkHandling = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecommendedTimes = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_network_presets", x => x.Network);
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
                name: "tax_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RatePercent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Inclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NeedsReview = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rates", x => x.Id);
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
                name: "website_blog_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_blog_categories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_consultation_blackouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_consultation_blackouts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_consultation_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SlotMinutes = table.Column<int>(type: "int", nullable: false),
                    MinNoticeHours = table.Column<int>(type: "int", nullable: false),
                    MaxDaysAhead = table.Column<int>(type: "int", nullable: false),
                    WeeklyAvailability = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_consultation_settings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_cv_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<byte[]>(type: "longblob", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_cv_files", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_industries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BodyMarkdown = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Challenges = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeroImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoTitle = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoOgImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoCanonicalUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoNoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_industries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_job_openings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Department = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Location = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Workplace = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmploymentType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DescriptionMarkdown = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Requirements = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Benefits = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SalaryMin = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    SalaryMax = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    SalaryCurrency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SalaryPeriod = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_job_openings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_newsletter_subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmTokenExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    UnsubscribeTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    UnsubscribedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_newsletter_subscribers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BlocksJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoTitle = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoOgImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoCanonicalUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoNoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_pages", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_service_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_service_categories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_settings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_team_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Bio = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhotoUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Expertise = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SocialLinks = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_team_members", x => x.Id);
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
                name: "client_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Industry = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusChangedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AccountManagerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LogoFileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Summary = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingContactName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingAddress = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaxId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CrmCompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovalSlaDays = table.Column<int>(type: "int", nullable: false),
                    AutoApproveAfterDays = table.Column<int>(type: "int", nullable: true),
                    LastInvoicePaidAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_saved_views",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Entity = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FiltersJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Shared = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_saved_views", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_saved_views_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "timesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DecisionComment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalMinutes = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timesheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_timesheets_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "website_inquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Company = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceSlugs = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PackageIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetRange = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeline = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmTerm = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmContent = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Referrer = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LandingPath = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedToUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    StaffNotes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_inquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_inquiries_users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_case_studies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAnonymized = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Summary = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IndustryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ServiceIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengeMarkdown = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StrategyMarkdown = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutionMarkdown = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Metrics = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TestimonialQuote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TestimonialAuthor = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TestimonialRole = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CoverImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GalleryImageUrls = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoTitle = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoOgImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoCanonicalUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoNoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFeatured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_case_studies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_case_studies_website_industries_IndustryId",
                        column: x => x.IndustryId,
                        principalTable: "website_industries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_job_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    JobOpeningId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PortfolioUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CoverLetter = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CvFileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Stage = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ConsentVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_job_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_job_applications_website_cv_files_CvFileId",
                        column: x => x.CvFileId,
                        principalTable: "website_cv_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_website_job_applications_website_job_openings_JobOpeningId",
                        column: x => x.JobOpeningId,
                        principalTable: "website_job_openings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CategoryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tagline = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeroTitle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeroBody = table.Column<string>(type: "varchar(600)", maxLength: 600, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OverviewMarkdown = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProblemsSolved = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Deliverables = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProcessSteps = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tools = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kpis = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Faqs = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedServiceIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Icon = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HeroImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CtaLabel = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CtaUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoTitle = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoOgImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoCanonicalUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoNoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFeatured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_services_website_service_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "website_service_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_blog_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(180)", maxLength: 180, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Excerpt = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BodyMarkdown = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CoverImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CoverImageAlt = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CategoryIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReadingMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RelatedPostIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoTitle = table.Column<string>(type: "varchar(70)", maxLength: 70, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoDescription = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoOgImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoCanonicalUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeoNoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PublishedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_blog_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_blog_posts_website_team_members_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "website_team_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                name: "ads_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalAccountId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IntegrationConnectionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ManagerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastSyncMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_accounts_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupeKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EvaluatedFor = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AcknowledgedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_alerts_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OverPacingThreshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnderPacingThreshold = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TargetCpa = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    TargetRoas = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_budgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_budgets_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_client_settings",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignNamingTemplate = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultUtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultUtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LowercaseUtm = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_client_settings", x => x.ClientAccountId);
                    table.ForeignKey(
                        name: "FK_ads_client_settings_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_creatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Format = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Headlines = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Descriptions = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrimaryText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CallToAction = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FinalUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaAssetIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_creatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_creatives_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hypothesis = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Metric = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Result = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WinnerVariant = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnteredPValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_experiments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_experiments_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_media_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_media_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_media_plans_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_utm_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BaseUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Medium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Campaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Term = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaggedUrl = table.Column<string>(type: "varchar(3000)", maxLength: 3000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_utm_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_utm_links_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "brand_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brand_assets_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "brand_kits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Colors = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fonts = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ToneOfVoice = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Personas = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Competitors = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Dos = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Donts = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KeyMessages = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_kits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brand_kits_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplateKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Answers = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmittedByClient = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConvertedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    StaffNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_briefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_briefs_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeliverableId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Period = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupeKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_feedback_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_meetings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Agenda = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttendeeUserIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActionItems = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_meetings_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_members",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_onboarding_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Owner = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_onboarding_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_onboarding_items_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TemplateKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sections = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AutoKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_reports_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "client_team_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ServiceRole = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_team_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_team_assignments_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_team_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Domain = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Industry = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Size = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "char(2)", fixedLength: true, maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TagIndex = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CustomFieldsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_companies_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_companies_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "delivery_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StorageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginalFileName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_files_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_automations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Trigger = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TriggerConfigJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reentry = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReentryCooldownDays = table.Column<int>(type: "int", nullable: false),
                    GoalJson = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderProfileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    EntryStepKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeedKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastAnniversaryScan = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_automations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_automations_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SegmentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SenderProfileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviewText = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DesignJson = table.Column<string>(type: "longtext", maxLength: 200000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Topic = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SmsBody = table.Column<string>(type: "varchar(1600)", maxLength: 1600, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppTemplateName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppTemplateLanguage = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppParametersJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduleMode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ScheduledLocalTime = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SendWindowStartHour = table.Column<int>(type: "int", nullable: true),
                    SendWindowEndHour = table.Column<int>(type: "int", nullable: true),
                    ThrottlePerMinute = table.Column<int>(type: "int", nullable: false),
                    AbTestPercent = table.Column<int>(type: "int", nullable: false),
                    AbWinnerMetric = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AbWaitHours = table.Column<int>(type: "int", nullable: false),
                    AbWinnerVariant = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AbTestCompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AbDecidedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovalDecidedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovalDecidedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovalNote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SendConfirmedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SendConfirmedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SendStartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ExpandedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PausedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PauseReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_campaigns_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DoubleOptIn = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ShowInPreferenceCenter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublicKey = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentTextVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SeedKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_lists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_lists_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefinitionJson = table.Column<string>(type: "longtext", maxLength: 20000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastCount = table.Column<int>(type: "int", nullable: true),
                    LastCountedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_segments_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_sender_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReplyTo = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VerificationCodeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerificationSentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    VerificationAttempts = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_sender_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_sender_profiles_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Language = table.Column<string>(type: "varchar(12)", maxLength: 12, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConsent = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SmsConsent = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SmsConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    WhatsAppConsent = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WhatsAppConsentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Frequency = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastOpenAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastClickAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SoftBounceCount = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_subscribers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_subscribers_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_suppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_suppressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_suppressions_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviewText = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DesignJson = table.Column<string>(type: "longtext", maxLength: 200000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SeedKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_templates_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_workspace_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ScopeKey = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhysicalAddress = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequireClientApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EmailProvider = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultThrottlePerMinute = table.Column<int>(type: "int", nullable: false),
                    QuietHoursStart = table.Column<int>(type: "int", nullable: false),
                    QuietHoursEnd = table.Column<int>(type: "int", nullable: false),
                    DefaultTimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SmsCostPerSegment = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    WhatsAppCostPerMessage = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CostCurrency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_workspace_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_workspace_settings_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SchemaJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmitLabel = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SuccessMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RedirectUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NotifyUserIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoresponderEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AutoresponderSubject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoresponderBody = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllowedOrigins = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentVersion = table.Column<int>(type: "int", nullable: false),
                    Captcha = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinFillSeconds = table.Column<int>(type: "int", nullable: false),
                    TemplateKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_forms_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Provider = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DisplayName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettingsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EncryptedSecrets = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastVerifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_connections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_connections_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "landing_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetaTitle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetaDescription = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OgImageUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NoIndex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TemplateKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VariantsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExperimentEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExperimentStartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedVersionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    HasUnpublishedChanges = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landing_pages_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "message_threads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LastMessageAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    MessageCount = table.Column<int>(type: "int", nullable: false),
                    LastMessagePreview = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastAuthorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_threads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_message_threads_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceLines = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BudgetHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BudgetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DefaultHourlyRate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TemplateKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Domain = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Protocol = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SitemapUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetCountry = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetLanguage = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Competitors = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxPages = table.Column<int>(type: "int", nullable: false),
                    MaxDepth = table.Column<int>(type: "int", nullable: false),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_sites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_sites_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_brand_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Handle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConnectionStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusMessage = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IntegrationConnectionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TokenExpiresAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_brand_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_brand_profiles_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmContent = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmTerm = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_campaigns_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_caption_snippets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_caption_snippets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_caption_snippets_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_client_settings",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequireClientApproval = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DefaultUtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_client_settings", x => x.ClientAccountId);
                    table.ForeignKey(
                        name: "FK_sm_client_settings_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_competitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Handle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProfileUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_competitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_competitors_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_hashtag_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hashtags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_hashtag_sets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_hashtag_sets_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_inbox_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorHandle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedToUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Sentiment = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupeKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_inbox_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_inbox_items_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_listening_queries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Term = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Networks = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_listening_queries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_listening_queries_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ExternalUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    AltText = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_media_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_media_assets_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_mentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QueryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorHandle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Sentiment = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentimentSource = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentimentScore = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupeKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_mentions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_mentions_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_metric_imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowsTotal = table.Column<int>(type: "int", nullable: false),
                    RowsImported = table.Column<int>(type: "int", nullable: false),
                    RowsUpdated = table.Column<int>(type: "int", nullable: false),
                    RowsSkipped = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_metric_imports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_metric_imports_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScheduledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AutoAppendUtm = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsEvergreen = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EvergreenIntervalDays = table.Column<int>(type: "int", nullable: false),
                    EvergreenMaxRepeats = table.Column<int>(type: "int", nullable: false),
                    EvergreenRepeatCount = table.Column<int>(type: "int", nullable: false),
                    RecycledFromPostId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RecycleNumber = table.Column<int>(type: "int", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishClaimId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PublishClaimedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FailureReason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_posts_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
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
                name: "payout_item_earnings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PayoutItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EarningEntryId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SettlementAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
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
                name: "website_consultation_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SlotStart = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SlotEnd = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SlotKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Company = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceSlugs = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VisitorTimeZone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InquiryId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CancellationReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_consultation_bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_consultation_bookings_website_inquiries_InquiryId",
                        column: x => x.InquiryId,
                        principalTable: "website_inquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_job_application_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ApplicationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StageFrom = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StageTo = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_job_application_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_job_application_notes_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_website_job_application_notes_website_job_applications_Appli~",
                        column: x => x.ApplicationId,
                        principalTable: "website_job_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_service_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ServiceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BillingPeriod = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SetupFee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Features = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsMostPopular = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsCustomQuote = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_service_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_service_packages_website_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "website_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "website_testimonials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Quote = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorRole = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Company = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    AvatarUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsPublished = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFeatured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_testimonials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_testimonials_website_services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "website_services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                    NormalizedPostUrl = table.Column<string>(type: "varchar(768)", maxLength: 768, nullable: false, collation: "utf8mb4_bin")
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
                name: "ads_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExternalId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Objective = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BidStrategy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetingSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetCpa = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    TargetRoas = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NamingCompliant = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_campaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_campaigns_ads_accounts_AdAccountId",
                        column: x => x.AdAccountId,
                        principalTable: "ads_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_daily_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityName = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AdGroupId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AdId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Spend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Conversions = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ConversionValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: true),
                    VideoViews = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImportBatchId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_daily_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_daily_metrics_ads_accounts_AdAccountId",
                        column: x => x.AdAccountId,
                        principalTable: "ads_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_import_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Template = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowsTotal = table.Column<int>(type: "int", nullable: false),
                    RowsImported = table.Column<int>(type: "int", nullable: false),
                    RowsUpdated = table.Column<int>(type: "int", nullable: false),
                    RowsSkipped = table.Column<int>(type: "int", nullable: false),
                    Errors = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_import_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_import_batches_ads_accounts_AdAccountId",
                        column: x => x.AdAccountId,
                        principalTable: "ads_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_experiment_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsControl = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Conversions = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Spend = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_experiment_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_experiment_variants_ads_experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "ads_experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_media_plan_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Objective = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PlannedBudget = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    FlightStart = table.Column<DateOnly>(type: "date", nullable: false),
                    FlightEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    KpiName = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KpiTarget = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    PlannedImpressions = table.Column<long>(type: "bigint", nullable: true),
                    PlannedClicks = table.Column<long>(type: "bigint", nullable: true),
                    PlannedConversions = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_media_plan_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_media_plan_lines_ads_media_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ads_media_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FirstName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    JobTitle = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LifecycleStage = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConsentStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentChangedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TagIndex = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetRange = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<int>(type: "int", nullable: false),
                    ScoredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FirstTouchSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastTouchSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_contacts_crm_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "crm_companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_contacts_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_automation_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AutomationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfigJson = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AltNextKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_automation_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_automation_steps_email_automations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "email_automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_campaign_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviewText = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderProfileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DesignJson = table.Column<string>(type: "longtext", maxLength: 200000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_campaign_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_campaign_variants_email_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "email_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ListId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CsvContent = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MappingJson = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TagsJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentAttestation = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentSource = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrantSmsConsent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AttestedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ProcessedRows = table.Column<int>(type: "int", nullable: false),
                    Created = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Skipped = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    ErrorsJson = table.Column<string>(type: "longtext", maxLength: 100000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_imports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_imports_email_lists_ListId",
                        column: x => x.ListId,
                        principalTable: "email_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_automation_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AutomationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Iteration = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentStepKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextRunAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ExitReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LockedUntil = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    StepsExecuted = table.Column<int>(type: "int", nullable: false),
                    TriggerDataJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_automation_enrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_automation_enrollments_email_automations_AutomationId",
                        column: x => x.AutomationId,
                        principalTable: "email_automations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_automation_enrollments_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_campaign_recipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Variant = table.Column<string>(type: "varchar(2)", maxLength: 2, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsTestCohort = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Address = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SkipReason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ClaimId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LockedUntil = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Error = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    BounceType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BouncedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    OpenCount = table.Column<int>(type: "int", nullable: false),
                    MachineOpenCount = table.Column<int>(type: "int", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ClickCount = table.Column<int>(type: "int", nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ComplainedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConvertedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Segments = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_campaign_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_campaign_recipients_email_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "email_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_campaign_recipients_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_consent_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentTextVersion = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_consent_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_consent_records_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RecipientId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AutomationId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AutomationStepRunId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LinkId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IsMachine = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Device = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MailClient = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalReference = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DedupKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_events_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_list_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ListId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    UnsubscribedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConfirmationSentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_list_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_list_memberships_email_lists_ListId",
                        column: x => x.ListId,
                        principalTable: "email_lists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_list_memberships_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_subscriber_fields",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_subscriber_fields", x => new { x.SubscriberId, x.Key });
                    table.ForeignKey(
                        name: "FK_email_subscriber_fields_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_subscriber_tags",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Tag = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_subscriber_tags", x => new { x.SubscriberId, x.Tag });
                    table.ForeignKey(
                        name: "FK_email_subscriber_tags_email_subscribers_SubscriberId",
                        column: x => x.SubscriberId,
                        principalTable: "email_subscribers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "form_consent_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FormId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_consent_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_consent_versions_forms_FormId",
                        column: x => x.FormId,
                        principalTable: "forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "form_submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FormId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LandingPageId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VariantKey = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DataJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmTerm = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmContent = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Referrer = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmbedOrigin = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsentVersion = table.Column<int>(type: "int", nullable: true),
                    ConsentGiven = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    EventPublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_submissions_forms_FormId",
                        column: x => x.FormId,
                        principalTable: "forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "landing_page_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubjectKey = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VariantKey = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landing_page_assignments_landing_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "landing_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "landing_page_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landing_page_versions_landing_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "landing_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "landing_page_views",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VariantKey = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExperimentId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VisitorHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ViewedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IsUnique = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReferrerHost = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UtmSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_views", x => x.Id);
                    table.ForeignKey(
                        name: "FK_landing_page_views_landing_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "landing_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "thread_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ThreadId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FromClient = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttachmentFileIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thread_messages_message_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "message_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "thread_read_states",
                columns: table => new
                {
                    ThreadId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LastReadAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thread_read_states", x => new { x.ThreadId, x.UserId });
                    table.ForeignKey(
                        name: "FK_thread_read_states_message_threads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "message_threads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members", x => new { x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_project_members_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_members_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ClientVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_milestones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_milestones_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "recurring_task_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DayOfMonth = table.Column<int>(type: "int", nullable: false),
                    DueInDays = table.Column<int>(type: "int", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    EstimateHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Labels = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_task_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_recurring_task_rules_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    QueuedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PagesCrawled = table.Column<int>(type: "int", nullable: false),
                    HealthScore = table.Column<int>(type: "int", nullable: true),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    NoticeCount = table.Column<int>(type: "int", nullable: false),
                    RobotsTxtFound = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SitemapFound = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FailureMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxPages = table.Column<int>(type: "int", nullable: false),
                    MaxDepth = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_audits_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_backlinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetUrl = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnchorText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastStatusCode = table.Column<int>(type: "int", nullable: true),
                    CheckMessage = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_backlinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_backlinks_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_citations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListingUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListedName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListedAddress = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ListedPhone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_citations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_citations_seo_citation_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "seo_citation_sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_seo_citations_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_content_briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetKeyword = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedKeywords = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Questions = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Outline = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WordCountTarget = table.Column<int>(type: "int", nullable: false),
                    CompetitorUrls = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_content_briefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_content_briefs_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_keywords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Keyword = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedKeyword = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Intent = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SearchVolume = table.Column<int>(type: "int", nullable: true),
                    Difficulty = table.Column<int>(type: "int", nullable: true),
                    TargetUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsTracked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_keywords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_keywords_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_local_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BusinessName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Phone = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Website = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedChecklist = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_local_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_local_profiles_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_outreach_prospects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProspectUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastContactedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_outreach_prospects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_outreach_prospects_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Platform = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    AuthorName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Responded = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ResponseText = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_reviews_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_search_performance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Query = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Page = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowHash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Ctr = table.Column<double>(type: "double", nullable: false),
                    Position = table.Column<double>(type: "double", nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_search_performance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_search_performance_seo_sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "seo_sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_post_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VariantId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PostKey = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: false),
                    Engagements = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    VideoViews = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImportBatchId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_post_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_post_metrics_sm_brand_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "sm_brand_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_profile_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Followers = table.Column<long>(type: "bigint", nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Reach = table.Column<long>(type: "bigint", nullable: false),
                    Engagements = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    VideoViews = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImportBatchId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_profile_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_profile_metrics_sm_brand_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "sm_brand_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_queue_slots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DayOfWeek = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinuteOfDay = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_queue_slots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_queue_slots_sm_brand_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "sm_brand_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_competitor_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CompetitorId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Followers = table.Column<long>(type: "bigint", nullable: false),
                    EngagementRate = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    PostsLast30Days = table.Column<int>(type: "int", nullable: true),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_competitor_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_competitor_snapshots_sm_competitors_CompetitorId",
                        column: x => x.CompetitorId,
                        principalTable: "sm_competitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_inbox_replies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ItemId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Body = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentViaApi = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_inbox_replies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_inbox_replies_sm_inbox_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "sm_inbox_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_post_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AuthorName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsClient = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsInternal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_post_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_post_comments_sm_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "sm_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_post_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProfileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AltTexts = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Link = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstComment = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Hashtags = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mentions = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishStatus = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    FailureKind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureReason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalPostId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PublishedManually = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MarkedPublishedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_post_variants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_post_variants_sm_brand_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "sm_brand_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sm_post_variants_sm_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "sm_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "sm_publish_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VariantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Network = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureKind = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExternalPostId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_publish_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sm_publish_attempts_sm_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "sm_posts",
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

            migrationBuilder.CreateTable(
                name: "ads_ad_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExternalId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    BidStrategy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetingSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_ad_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_ad_groups_ads_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "ads_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_deals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PrimaryContactId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    StageId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedCloseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ServiceSlugs = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceDetail = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BudgetRange = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FirstTouchAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastTouchSource = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchMedium = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchCampaign = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastTouchAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LostReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StageChangedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_deals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_deals_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_deals_crm_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "crm_companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_deals_crm_contacts_PrimaryContactId",
                        column: x => x.PrimaryContactId,
                        principalTable: "crm_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_deals_crm_pipeline_stages_StageId",
                        column: x => x.StageId,
                        principalTable: "crm_pipeline_stages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_crm_deals_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_engagements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_engagements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_engagements_crm_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "crm_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "email_automation_step_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrollmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AutomationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SubscriberId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StepKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StepType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Detail = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Channel = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderMessageId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Segments = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_automation_step_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_automation_step_runs_email_automation_enrollments_Enro~",
                        column: x => x.EnrollmentId,
                        principalTable: "email_automation_enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "form_submission_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubmissionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FormId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FieldKey = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_submission_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_form_submission_files_form_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "form_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    MilestoneId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Priority = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EstimateHours = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Labels = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<double>(type: "double", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RecurrenceKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BriefId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    MeetingId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_project_tasks_project_milestones_MilestoneId",
                        column: x => x.MilestoneId,
                        principalTable: "project_milestones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_tasks_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_audit_issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuditId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RuleKey = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AffectedCount = table.Column<int>(type: "int", nullable: false),
                    AffectedUrls = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Details = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_audit_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_audit_issues_seo_audits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "seo_audits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_audit_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuditId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "int", nullable: false),
                    ContentLength = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetaDescription = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    H1Count = table.Column<int>(type: "int", nullable: false),
                    WordCount = table.Column<int>(type: "int", nullable: false),
                    Canonical = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsNoindex = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    InSitemap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    InboundLinks = table.Column<int>(type: "int", nullable: false),
                    RedirectChain = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FetchError = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_audit_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_audit_pages_seo_audits_AuditId",
                        column: x => x.AuditId,
                        principalTable: "seo_audits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "seo_rank_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    KeywordId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SiteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Domain = table.Column<string>(type: "varchar(253)", maxLength: 253, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: true),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SerpFeatures = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_rank_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_seo_rank_snapshots_seo_keywords_KeywordId",
                        column: x => x.KeywordId,
                        principalTable: "seo_keywords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ads_ads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CampaignId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AdGroupId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExternalId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreativeId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ads_ads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ads_ads_ads_ad_groups_AdGroupId",
                        column: x => x.AdGroupId,
                        principalTable: "ads_ad_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DealId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    OccursAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RemindAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ReminderSentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    OverdueNotifiedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsSystem = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_crm_activities_crm_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "crm_companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_crm_activities_crm_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "crm_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_crm_activities_crm_deals_DealId",
                        column: x => x.DealId,
                        principalTable: "crm_deals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_crm_activities_users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_deal_contacts",
                columns: table => new
                {
                    DealId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_deal_contacts", x => new { x.DealId, x.ContactId });
                    table.ForeignKey(
                        name: "FK_crm_deal_contacts_crm_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "crm_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_crm_deal_contacts_crm_deals_DealId",
                        column: x => x.DealId,
                        principalTable: "crm_deals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DealId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    SentVersion = table.Column<int>(type: "int", nullable: true),
                    RecipientName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecipientEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InvoiceOnAcceptance = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    ShareTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ShareTokenProtected = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    SentByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ViewCount = table.Column<int>(type: "int", nullable: false),
                    FirstViewedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastViewedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AcceptedVersion = table.Column<int>(type: "int", nullable: true),
                    SignerName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignerTitle = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignerEmail = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignerIpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignerUserAgent = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AcceptedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DeclinedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DeclineReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposals_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_proposals_crm_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "crm_companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_proposals_crm_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "crm_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_proposals_crm_deals_DealId",
                        column: x => x.DealId,
                        principalTable: "crm_deals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deliverables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    LastSentVersion = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ReviewerUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SentToClientAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ClientDueAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    ApprovedVersion = table.Column<int>(type: "int", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AutoApproved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliverables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliverables_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_deliverables_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_assignees",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_assignees", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_assignees_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_assignees_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AddedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_attachments_delivery_files_FileId",
                        column: x => x.FileId,
                        principalTable: "delivery_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_attachments_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Text = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDone = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_checklist_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_checklist_items_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MentionedUserIds = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_task_comments_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_dependencies",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    BlockedByTaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_dependencies", x => new { x.TaskId, x.BlockedByTaskId });
                    table.ForeignKey(
                        name: "FK_task_dependencies_project_tasks_BlockedByTaskId",
                        column: x => x.BlockedByTaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_dependencies_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "task_watchers",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_task_watchers", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_task_watchers_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_task_watchers_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TaskId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    Billable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RunningUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_time_entries_project_tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "project_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_time_entries_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_entries_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BillingFrequency = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoRenew = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RenewalTermMonths = table.Column<int>(type: "int", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "int", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    AutoIssueInvoices = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    NextPeriodIndex = table.Column<int>(type: "int", nullable: false),
                    ProposalId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ProposalVersion = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CancelReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contracts_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contracts_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "proposal_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProposalId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Goals = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scope = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Deliverables = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeline = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Terms = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GrossTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    OneTimeTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    MonthlyRecurringValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    FirstYearValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    Locked = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_versions_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deliverable_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeliverableId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    FromClient = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsInternal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Body = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliverable_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliverable_comments_deliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "deliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deliverable_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeliverableId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Decision = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    UserName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Comment = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliverable_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliverable_reviews_deliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "deliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "deliverable_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DeliverableId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LinkUrl = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Body = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliverable_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliverable_versions_deliverables_DeliverableId",
                        column: x => x.DeliverableId,
                        principalTable: "deliverables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_deliverable_versions_delivery_files_FileId",
                        column: x => x.FileId,
                        principalTable: "delivery_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "contract_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ContractId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceSlug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TaxName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaxPercent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contract_lines_contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_contract_lines_tax_rates_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "tax_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "int", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountCredited = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountWrittenOff = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContractId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ProposalId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublicTokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublicTokenProtected = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssuedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VoidReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WrittenOffAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    WrittenOffByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    WriteOffReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoices_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoices_proposals_ProposalId",
                        column: x => x.ProposalId,
                        principalTable: "proposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "proposal_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProposalVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PackageSlug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Recurrence = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceSlug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TaxName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaxPercent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposal_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_proposal_lines_proposal_versions_ProposalVersionId",
                        column: x => x.ProposalVersionId,
                        principalTable: "proposal_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_proposal_lines_tax_rates_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "tax_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InvoiceId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AmountApplied = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_notes_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_notes_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InvoiceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ServiceSlug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DiscountValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    TaxName = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TaxPercent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invoice_lines_tax_rates_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "tax_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InvoiceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClientAccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "char(3)", fixedLength: true, maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Method = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RecordedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_payments_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_payments_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_invoice_payments_users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoice_reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InvoiceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SentAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_reminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_reminders_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "credit_note_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreditNoteId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InvoiceId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_note_applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credit_note_applications_credit_notes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalTable: "credit_notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_credit_note_applications_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_achievements_Key",
                table: "achievements",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ads_accounts_ClientAccountId_Platform_ExternalAccountId",
                table: "ads_accounts",
                columns: new[] { "ClientAccountId", "Platform", "ExternalAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ads_ad_groups_CampaignId_ExternalId",
                table: "ads_ad_groups",
                columns: new[] { "CampaignId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_ad_groups_CampaignId_Name",
                table: "ads_ad_groups",
                columns: new[] { "CampaignId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_ads_AdGroupId_ExternalId",
                table: "ads_ads",
                columns: new[] { "AdGroupId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_ads_AdGroupId_Name",
                table: "ads_ads",
                columns: new[] { "AdGroupId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_alerts_ClientAccountId_Status_CreatedAt",
                table: "ads_alerts",
                columns: new[] { "ClientAccountId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_alerts_DedupeKey",
                table: "ads_alerts",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ads_budgets_ClientAccountId_Month",
                table: "ads_budgets",
                columns: new[] { "ClientAccountId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_budgets_Month",
                table: "ads_budgets",
                column: "Month");

            migrationBuilder.CreateIndex(
                name: "IX_ads_campaigns_AdAccountId_ExternalId",
                table: "ads_campaigns",
                columns: new[] { "AdAccountId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_campaigns_AdAccountId_Name",
                table: "ads_campaigns",
                columns: new[] { "AdAccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_campaigns_ClientAccountId",
                table: "ads_campaigns",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_creatives_ClientAccountId_Status",
                table: "ads_creatives",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_daily_metrics_AdAccountId_Date_Level_EntityKey",
                table: "ads_daily_metrics",
                columns: new[] { "AdAccountId", "Date", "Level", "EntityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ads_daily_metrics_CampaignId_Date",
                table: "ads_daily_metrics",
                columns: new[] { "CampaignId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_daily_metrics_ClientAccountId_Level_Date",
                table: "ads_daily_metrics",
                columns: new[] { "ClientAccountId", "Level", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_experiment_variants_ExperimentId",
                table: "ads_experiment_variants",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_experiments_ClientAccountId",
                table: "ads_experiments",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_import_batches_AdAccountId_CreatedAt",
                table: "ads_import_batches",
                columns: new[] { "AdAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_media_plan_lines_PlanId",
                table: "ads_media_plan_lines",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_ads_media_plans_ClientAccountId_Month",
                table: "ads_media_plans",
                columns: new[] { "ClientAccountId", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_ads_utm_links_ClientAccountId_CreatedAt",
                table: "ads_utm_links",
                columns: new[] { "ClientAccountId", "CreatedAt" });

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
                name: "IX_billing_number_sequences_Key",
                table: "billing_number_sequences",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brand_assets_ClientAccountId",
                table: "brand_assets",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_brand_kits_ClientAccountId",
                table: "brand_kits",
                column: "ClientAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_templates_Key",
                table: "brief_templates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_briefs_ClientAccountId_Status",
                table: "briefs",
                columns: new[] { "ClientAccountId", "Status" });

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
                name: "IX_client_feedback_ClientAccountId_Kind_CreatedAt",
                table: "client_feedback",
                columns: new[] { "ClientAccountId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_client_feedback_DedupeKey",
                table: "client_feedback",
                column: "DedupeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_meetings_ClientAccountId_StartsAt",
                table: "client_meetings",
                columns: new[] { "ClientAccountId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_client_meetings_StartsAt",
                table: "client_meetings",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_client_members_UserId",
                table: "client_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_client_onboarding_items_ClientAccountId_Key",
                table: "client_onboarding_items",
                columns: new[] { "ClientAccountId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_reports_AutoKey",
                table: "client_reports",
                column: "AutoKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_reports_ClientAccountId_PeriodStart",
                table: "client_reports",
                columns: new[] { "ClientAccountId", "PeriodStart" });

            migrationBuilder.CreateIndex(
                name: "IX_client_team_assignments_ClientAccountId_UserId_ServiceRole",
                table: "client_team_assignments",
                columns: new[] { "ClientAccountId", "UserId", "ServiceRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_team_assignments_UserId",
                table: "client_team_assignments",
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
                name: "IX_contract_lines_ContractId_Position",
                table: "contract_lines",
                columns: new[] { "ContractId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_contract_lines_TaxRateId",
                table: "contract_lines",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_ClientAccountId_Status",
                table: "contracts",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_Number",
                table: "contracts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contracts_ProposalId_BillingFrequency",
                table: "contracts",
                columns: new[] { "ProposalId", "BillingFrequency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contracts_Status",
                table: "contracts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_applications_CreditNoteId",
                table: "credit_note_applications",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_note_applications_InvoiceId",
                table: "credit_note_applications",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_ClientAccountId_Status",
                table: "credit_notes",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_InvoiceId",
                table: "credit_notes",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_Number",
                table: "credit_notes",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credit_notes_RequestId",
                table: "credit_notes",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_AssigneeUserId_CompletedAt_DueAt",
                table: "crm_activities",
                columns: new[] { "AssigneeUserId", "CompletedAt", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_CompanyId_CreatedAt",
                table: "crm_activities",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_CompletedAt_DueAt_OverdueNotifiedAt",
                table: "crm_activities",
                columns: new[] { "CompletedAt", "DueAt", "OverdueNotifiedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_ContactId_CreatedAt",
                table: "crm_activities",
                columns: new[] { "ContactId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_DealId_CreatedAt",
                table: "crm_activities",
                columns: new[] { "DealId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_activities_RemindAt_ReminderSentAt",
                table: "crm_activities",
                columns: new[] { "RemindAt", "ReminderSentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_assignment_cursors_Pool",
                table: "crm_assignment_cursors",
                column: "Pool",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_ClientAccountId",
                table: "crm_companies",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_Domain",
                table: "crm_companies",
                column: "Domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_Name",
                table: "crm_companies",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_crm_companies_OwnerUserId",
                table: "crm_companies",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_CompanyId",
                table: "crm_contacts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_CreatedAt",
                table: "crm_contacts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_LifecycleStage",
                table: "crm_contacts",
                column: "LifecycleStage");

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_NormalizedEmail",
                table: "crm_contacts",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_contacts_OwnerUserId",
                table: "crm_contacts",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deal_contacts_ContactId",
                table: "crm_deal_contacts",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_ClientAccountId",
                table: "crm_deals",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_ClosedAt",
                table: "crm_deals",
                column: "ClosedAt");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_CompanyId",
                table: "crm_deals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_OwnerUserId",
                table: "crm_deals",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_PrimaryContactId",
                table: "crm_deals",
                column: "PrimaryContactId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_StageId",
                table: "crm_deals",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_crm_deals_Status_StageId",
                table: "crm_deals",
                columns: new[] { "Status", "StageId" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_engagements_ContactId_Type",
                table: "crm_engagements",
                columns: new[] { "ContactId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_engagements_SourceKey",
                table: "crm_engagements",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_pipeline_stages_Position",
                table: "crm_pipeline_stages",
                column: "Position");

            migrationBuilder.CreateIndex(
                name: "IX_crm_saved_views_Entity_OwnerUserId",
                table: "crm_saved_views",
                columns: new[] { "Entity", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_saved_views_OwnerUserId",
                table: "crm_saved_views",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_deliverable_comments_DeliverableId_VersionNumber",
                table: "deliverable_comments",
                columns: new[] { "DeliverableId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_deliverable_reviews_DeliverableId_CreatedAt",
                table: "deliverable_reviews",
                columns: new[] { "DeliverableId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deliverable_versions_DeliverableId_Number",
                table: "deliverable_versions",
                columns: new[] { "DeliverableId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliverable_versions_FileId",
                table: "deliverable_versions",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_deliverables_ClientAccountId_Status",
                table: "deliverables",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_deliverables_ProjectId",
                table: "deliverables",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_deliverables_Status_ClientDueAt",
                table: "deliverables",
                columns: new[] { "Status", "ClientDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_deliverables_TaskId",
                table: "deliverables",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_files_ClientAccountId",
                table: "delivery_files",
                column: "ClientAccountId");

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
                name: "IX_email_automation_enrollments_AutomationId_SubscriberId_Itera~",
                table: "email_automation_enrollments",
                columns: new[] { "AutomationId", "SubscriberId", "Iteration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_enrollments_Status_NextRunAt",
                table: "email_automation_enrollments",
                columns: new[] { "Status", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_enrollments_SubscriberId",
                table: "email_automation_enrollments",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_step_runs_AutomationId_StepKey",
                table: "email_automation_step_runs",
                columns: new[] { "AutomationId", "StepKey" });

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_step_runs_EnrollmentId_StepKey",
                table: "email_automation_step_runs",
                columns: new[] { "EnrollmentId", "StepKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_step_runs_ProviderMessageId",
                table: "email_automation_step_runs",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_email_automation_steps_AutomationId_Key",
                table: "email_automation_steps",
                columns: new[] { "AutomationId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_automations_ClientAccountId",
                table: "email_automations",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_automations_ScopeKey_SeedKey",
                table: "email_automations",
                columns: new[] { "ScopeKey", "SeedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_automations_Status_Trigger",
                table: "email_automations",
                columns: new[] { "Status", "Trigger" });

            migrationBuilder.CreateIndex(
                name: "IX_email_campaign_recipients_CampaignId_Status_DueAt",
                table: "email_campaign_recipients",
                columns: new[] { "CampaignId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_campaign_recipients_CampaignId_SubscriberId",
                table: "email_campaign_recipients",
                columns: new[] { "CampaignId", "SubscriberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_campaign_recipients_ProviderMessageId",
                table: "email_campaign_recipients",
                column: "ProviderMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_email_campaign_recipients_SubscriberId_SentAt",
                table: "email_campaign_recipients",
                columns: new[] { "SubscriberId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_campaign_variants_CampaignId_Key",
                table: "email_campaign_variants",
                columns: new[] { "CampaignId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_campaigns_ClientAccountId",
                table: "email_campaigns",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_campaigns_ScopeKey_Status",
                table: "email_campaigns",
                columns: new[] { "ScopeKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_email_campaigns_Status_ScheduledAt",
                table: "email_campaigns",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_consent_records_SubscriberId_RecordedAt",
                table: "email_consent_records",
                columns: new[] { "SubscriberId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_events_AutomationId_Type",
                table: "email_events",
                columns: new[] { "AutomationId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_email_events_CampaignId_Type",
                table: "email_events",
                columns: new[] { "CampaignId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_email_events_ClientAccountId_Type_OccurredAt",
                table: "email_events",
                columns: new[] { "ClientAccountId", "Type", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_events_DedupKey",
                table: "email_events",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_events_SubscriberId_Type_OccurredAt",
                table: "email_events",
                columns: new[] { "SubscriberId", "Type", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_imports_ListId",
                table: "email_imports",
                column: "ListId");

            migrationBuilder.CreateIndex(
                name: "IX_email_imports_Status",
                table: "email_imports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_email_list_memberships_ListId_Status",
                table: "email_list_memberships",
                columns: new[] { "ListId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_email_list_memberships_ListId_SubscriberId",
                table: "email_list_memberships",
                columns: new[] { "ListId", "SubscriberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_list_memberships_SubscriberId",
                table: "email_list_memberships",
                column: "SubscriberId");

            migrationBuilder.CreateIndex(
                name: "IX_email_lists_ClientAccountId",
                table: "email_lists",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_lists_PublicKey",
                table: "email_lists",
                column: "PublicKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_lists_ScopeKey_SeedKey",
                table: "email_lists",
                columns: new[] { "ScopeKey", "SeedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_segments_ClientAccountId",
                table: "email_segments",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_segments_ScopeKey",
                table: "email_segments",
                column: "ScopeKey");

            migrationBuilder.CreateIndex(
                name: "IX_email_sender_profiles_ClientAccountId",
                table: "email_sender_profiles",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_sender_profiles_ScopeKey",
                table: "email_sender_profiles",
                column: "ScopeKey");

            migrationBuilder.CreateIndex(
                name: "IX_email_subscriber_fields_Key",
                table: "email_subscriber_fields",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_email_subscriber_tags_Tag",
                table: "email_subscriber_tags",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_email_subscribers_ClientAccountId",
                table: "email_subscribers",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_subscribers_ScopeKey_NormalizedEmail",
                table: "email_subscribers",
                columns: new[] { "ScopeKey", "NormalizedEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_subscribers_ScopeKey_Phone",
                table: "email_subscribers",
                columns: new[] { "ScopeKey", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_email_subscribers_ScopeKey_Status",
                table: "email_subscribers",
                columns: new[] { "ScopeKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_email_suppressions_ClientAccountId",
                table: "email_suppressions",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_suppressions_ScopeKey_Channel_Value",
                table: "email_suppressions",
                columns: new[] { "ScopeKey", "Channel", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_suppressions_ScopeKey_CreatedAt",
                table: "email_suppressions",
                columns: new[] { "ScopeKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_email_templates_ClientAccountId",
                table: "email_templates",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_templates_ScopeKey_SeedKey",
                table: "email_templates",
                columns: new[] { "ScopeKey", "SeedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_tracked_links_SourceKey_UrlHash",
                table: "email_tracked_links",
                columns: new[] { "SourceKey", "UrlHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_workspace_settings_ClientAccountId",
                table: "email_workspace_settings",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_email_workspace_settings_ScopeKey",
                table: "email_workspace_settings",
                column: "ScopeKey",
                unique: true);

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
                name: "IX_form_consent_versions_FormId_Version",
                table: "form_consent_versions",
                columns: new[] { "FormId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_email_outbox_Key",
                table: "form_email_outbox",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_form_email_outbox_Status_NextAttemptAt",
                table: "form_email_outbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submission_files_SubmissionId",
                table: "form_submission_files",
                column: "SubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_ClientAccountId",
                table: "form_submissions",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_EventPublishedAt",
                table: "form_submissions",
                column: "EventPublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_FormId_SubmittedAt",
                table: "form_submissions",
                columns: new[] { "FormId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_LandingPageId_SubmittedAt",
                table: "form_submissions",
                columns: new[] { "LandingPageId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_forms_ClientAccountId",
                table: "forms",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_homepage_banners_IsActive_SortOrder",
                table: "homepage_banners",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_hourly_rates_Role",
                table: "hourly_rates",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_hourly_rates_UserId",
                table: "hourly_rates",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_ClientAccountId",
                table: "integration_connections",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_Provider_ClientAccountId",
                table: "integration_connections",
                columns: new[] { "Provider", "ClientAccountId" });

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
                name: "IX_invoice_lines_InvoiceId_Position",
                table: "invoice_lines",
                columns: new[] { "InvoiceId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_ServiceSlug",
                table: "invoice_lines",
                column: "ServiceSlug");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_TaxRateId",
                table: "invoice_lines",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_ClientAccountId_PaidOn",
                table: "invoice_payments",
                columns: new[] { "ClientAccountId", "PaidOn" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_InvoiceId_Reference",
                table: "invoice_payments",
                columns: new[] { "InvoiceId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_PaidOn",
                table: "invoice_payments",
                column: "PaidOn");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_RecordedByUserId",
                table: "invoice_payments",
                column: "RecordedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_RequestId",
                table: "invoice_payments",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_reminders_InvoiceId_Kind",
                table: "invoice_reminders",
                columns: new[] { "InvoiceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_ClientAccountId_Status",
                table: "invoices",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_invoices_ContractId",
                table: "invoices",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_IdempotencyKey",
                table: "invoices",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_IssueDate",
                table: "invoices",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_Number",
                table: "invoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_ProposalId",
                table: "invoices",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_PublicTokenHash",
                table: "invoices",
                column: "PublicTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_Status_DueDate",
                table: "invoices",
                columns: new[] { "Status", "DueDate" });

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
                name: "IX_landing_page_assignments_ExperimentId_SubjectKey",
                table: "landing_page_assignments",
                columns: new[] { "ExperimentId", "SubjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_assignments_PageId",
                table: "landing_page_assignments",
                column: "PageId");

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_versions_PageId_Version",
                table: "landing_page_versions",
                columns: new[] { "PageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_views_ClientAccountId",
                table: "landing_page_views",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_views_PageId_ViewedAt",
                table: "landing_page_views",
                columns: new[] { "PageId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_views_PageId_VisitorHash",
                table: "landing_page_views",
                columns: new[] { "PageId", "VisitorHash" });

            migrationBuilder.CreateIndex(
                name: "IX_landing_pages_ClientAccountId_Slug",
                table: "landing_pages",
                columns: new[] { "ClientAccountId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_landing_pages_Status",
                table: "landing_pages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_message_threads_ClientAccountId_LastMessageAt",
                table: "message_threads",
                columns: new[] { "ClientAccountId", "LastMessageAt" });

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
                name: "IX_project_members_UserId",
                table: "project_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_project_milestones_ProjectId",
                table: "project_milestones",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_project_tasks_ClientAccountId_DueDate",
                table: "project_tasks",
                columns: new[] { "ClientAccountId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_project_tasks_MilestoneId",
                table: "project_tasks",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_project_tasks_ProjectId_Status",
                table: "project_tasks",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_project_tasks_RecurrenceKey",
                table: "project_tasks",
                column: "RecurrenceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_templates_Key",
                table: "project_templates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_ClientAccountId_Status",
                table: "projects",
                columns: new[] { "ClientAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_OwnerUserId",
                table: "projects",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_lines_ProposalVersionId_Position",
                table: "proposal_lines",
                columns: new[] { "ProposalVersionId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_proposal_lines_TaxRateId",
                table: "proposal_lines",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_proposal_versions_ProposalId_VersionNumber",
                table: "proposal_versions",
                columns: new[] { "ProposalId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_ClientAccountId",
                table: "proposals",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_proposals_CompanyId",
                table: "proposals",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_proposals_ContactId",
                table: "proposals",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_proposals_DealId",
                table: "proposals",
                column: "DealId");

            migrationBuilder.CreateIndex(
                name: "IX_proposals_Number",
                table: "proposals",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_ShareTokenHash",
                table: "proposals",
                column: "ShareTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposals_Status_CreatedAt",
                table: "proposals",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_task_rules_ProjectId_IsActive",
                table: "recurring_task_rules",
                columns: new[] { "ProjectId", "IsActive" });

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
                name: "IX_report_templates_Key",
                table: "report_templates",
                column: "Key",
                unique: true);

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
                name: "IX_seo_audit_issues_AuditId_RuleKey",
                table: "seo_audit_issues",
                columns: new[] { "AuditId", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_audit_pages_AuditId",
                table: "seo_audit_pages",
                column: "AuditId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_audits_ClientAccountId",
                table: "seo_audits",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_audits_SiteId_QueuedAt",
                table: "seo_audits",
                columns: new[] { "SiteId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seo_audits_Status_QueuedAt",
                table: "seo_audits",
                columns: new[] { "Status", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seo_backlinks_ClientAccountId",
                table: "seo_backlinks",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_backlinks_SiteId_LinkHash",
                table: "seo_backlinks",
                columns: new[] { "SiteId", "LinkHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_backlinks_Status_LastCheckedAt",
                table: "seo_backlinks",
                columns: new[] { "Status", "LastCheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seo_citation_sources_Key",
                table: "seo_citation_sources",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_citations_ClientAccountId",
                table: "seo_citations",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_citations_SiteId_SourceId",
                table: "seo_citations",
                columns: new[] { "SiteId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_citations_SourceId",
                table: "seo_citations",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_content_briefs_ClientAccountId",
                table: "seo_content_briefs",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_content_briefs_SiteId",
                table: "seo_content_briefs",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_keywords_ClientAccountId",
                table: "seo_keywords",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_keywords_SiteId_NormalizedKeyword",
                table: "seo_keywords",
                columns: new[] { "SiteId", "NormalizedKeyword" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_local_profiles_SiteId",
                table: "seo_local_profiles",
                column: "SiteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_outreach_prospects_ClientAccountId",
                table: "seo_outreach_prospects",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_outreach_prospects_SiteId",
                table: "seo_outreach_prospects",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_rank_snapshots_ClientAccountId",
                table: "seo_rank_snapshots",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_rank_snapshots_KeywordId_Date_Domain",
                table: "seo_rank_snapshots",
                columns: new[] { "KeywordId", "Date", "Domain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_rank_snapshots_SiteId_Date",
                table: "seo_rank_snapshots",
                columns: new[] { "SiteId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_seo_reviews_ClientAccountId",
                table: "seo_reviews",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_reviews_SiteId_ReviewedAt",
                table: "seo_reviews",
                columns: new[] { "SiteId", "ReviewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_seo_search_performance_ClientAccountId",
                table: "seo_search_performance",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_seo_search_performance_SiteId_Date_RowHash",
                table: "seo_search_performance",
                columns: new[] { "SiteId", "Date", "RowHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_seo_sites_ClientAccountId_Domain",
                table: "seo_sites",
                columns: new[] { "ClientAccountId", "Domain" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_awareness_days_Month_Day_Name",
                table: "sm_awareness_days",
                columns: new[] { "Month", "Day", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_brand_profiles_ClientAccountId_Network_Handle",
                table: "sm_brand_profiles",
                columns: new[] { "ClientAccountId", "Network", "Handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_campaigns_ClientAccountId",
                table: "sm_campaigns",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_caption_snippets_ClientAccountId",
                table: "sm_caption_snippets",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_competitor_snapshots_CompetitorId_Date",
                table: "sm_competitor_snapshots",
                columns: new[] { "CompetitorId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_competitors_ClientAccountId_Network_Handle",
                table: "sm_competitors",
                columns: new[] { "ClientAccountId", "Network", "Handle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_hashtag_sets_ClientAccountId",
                table: "sm_hashtag_sets",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_inbox_items_ClientAccountId_DedupeKey",
                table: "sm_inbox_items",
                columns: new[] { "ClientAccountId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_inbox_items_ClientAccountId_Status_ReceivedAt",
                table: "sm_inbox_items",
                columns: new[] { "ClientAccountId", "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_inbox_replies_ItemId_CreatedAt",
                table: "sm_inbox_replies",
                columns: new[] { "ItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_listening_queries_ClientAccountId",
                table: "sm_listening_queries",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_media_assets_ClientAccountId_CreatedAt",
                table: "sm_media_assets",
                columns: new[] { "ClientAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_mentions_ClientAccountId_DedupeKey",
                table: "sm_mentions",
                columns: new[] { "ClientAccountId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_mentions_ClientAccountId_PostedAt",
                table: "sm_mentions",
                columns: new[] { "ClientAccountId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_metric_imports_ClientAccountId_CreatedAt",
                table: "sm_metric_imports",
                columns: new[] { "ClientAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_comments_PostId_CreatedAt",
                table: "sm_post_comments",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_metrics_ClientAccountId_Date",
                table: "sm_post_metrics",
                columns: new[] { "ClientAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_metrics_ProfileId_PostKey_Date",
                table: "sm_post_metrics",
                columns: new[] { "ProfileId", "PostKey", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_metrics_VariantId",
                table: "sm_post_metrics",
                column: "VariantId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_variants_ClientAccountId_ExternalPostId",
                table: "sm_post_variants",
                columns: new[] { "ClientAccountId", "ExternalPostId" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_variants_PostId",
                table: "sm_post_variants",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_post_variants_ProfileId",
                table: "sm_post_variants",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_posts_ClientAccountId_ScheduledAt",
                table: "sm_posts",
                columns: new[] { "ClientAccountId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_posts_RecycledFromPostId_RecycleNumber",
                table: "sm_posts",
                columns: new[] { "RecycledFromPostId", "RecycleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_posts_Status_ScheduledAt",
                table: "sm_posts",
                columns: new[] { "Status", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_profile_metrics_ClientAccountId_Date",
                table: "sm_profile_metrics",
                columns: new[] { "ClientAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_profile_metrics_ProfileId_Date",
                table: "sm_profile_metrics",
                columns: new[] { "ProfileId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_publish_attempts_ClientAccountId_StartedAt",
                table: "sm_publish_attempts",
                columns: new[] { "ClientAccountId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_publish_attempts_PostId_StartedAt",
                table: "sm_publish_attempts",
                columns: new[] { "PostId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sm_queue_slots_ClientAccountId",
                table: "sm_queue_slots",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_queue_slots_ProfileId_DayOfWeek_MinuteOfDay",
                table: "sm_queue_slots",
                columns: new[] { "ProfileId", "DayOfWeek", "MinuteOfDay" },
                unique: true);

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
                name: "IX_task_assignees_UserId",
                table: "task_assignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_FileId",
                table: "task_attachments",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_task_attachments_TaskId",
                table: "task_attachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_checklist_items_TaskId",
                table: "task_checklist_items",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_comments_TaskId_CreatedAt",
                table: "task_comments",
                columns: new[] { "TaskId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_task_dependencies_BlockedByTaskId",
                table: "task_dependencies",
                column: "BlockedByTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_task_watchers_UserId",
                table: "task_watchers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_thread_messages_ThreadId_CreatedAt",
                table: "thread_messages",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_ClientAccountId_Date",
                table: "time_entries",
                columns: new[] { "ClientAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_ProjectId_Date",
                table: "time_entries",
                columns: new[] { "ProjectId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_RunningUserId",
                table: "time_entries",
                column: "RunningUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_TaskId",
                table: "time_entries",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_UserId_Date",
                table: "time_entries",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_timesheets_Status_WeekStart",
                table: "timesheets",
                columns: new[] { "Status", "WeekStart" });

            migrationBuilder.CreateIndex(
                name: "IX_timesheets_UserId_WeekStart",
                table: "timesheets",
                columns: new[] { "UserId", "WeekStart" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_website_blog_categories_Slug",
                table: "website_blog_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_blog_posts_AuthorId",
                table: "website_blog_posts",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_website_blog_posts_Slug",
                table: "website_blog_posts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_blog_posts_Status_PublishAt",
                table: "website_blog_posts",
                columns: new[] { "Status", "PublishAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_blog_posts_Status_PublishedAt",
                table: "website_blog_posts",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_case_studies_IndustryId",
                table: "website_case_studies",
                column: "IndustryId");

            migrationBuilder.CreateIndex(
                name: "IX_website_case_studies_IsPublished_SortOrder",
                table: "website_case_studies",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_case_studies_Slug",
                table: "website_case_studies",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_consultation_blackouts_Date",
                table: "website_consultation_blackouts",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_consultation_bookings_InquiryId",
                table: "website_consultation_bookings",
                column: "InquiryId");

            migrationBuilder.CreateIndex(
                name: "IX_website_consultation_bookings_SlotKey",
                table: "website_consultation_bookings",
                column: "SlotKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_consultation_bookings_Status_SlotStart",
                table: "website_consultation_bookings",
                columns: new[] { "Status", "SlotStart" });

            migrationBuilder.CreateIndex(
                name: "IX_website_consultation_settings_Key",
                table: "website_consultation_settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_industries_IsPublished_SortOrder",
                table: "website_industries",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_industries_Slug",
                table: "website_industries",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_inquiries_AssignedToUserId",
                table: "website_inquiries",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_website_inquiries_Email",
                table: "website_inquiries",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_website_inquiries_Status_CreatedAt",
                table: "website_inquiries",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_inquiries_Type_CreatedAt",
                table: "website_inquiries",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_job_application_notes_ApplicationId_CreatedAt",
                table: "website_job_application_notes",
                columns: new[] { "ApplicationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_job_application_notes_AuthorUserId",
                table: "website_job_application_notes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_website_job_applications_CreatedAt",
                table: "website_job_applications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_website_job_applications_CvFileId",
                table: "website_job_applications",
                column: "CvFileId");

            migrationBuilder.CreateIndex(
                name: "IX_website_job_applications_JobOpeningId_Stage",
                table: "website_job_applications",
                columns: new[] { "JobOpeningId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_website_job_openings_Slug",
                table: "website_job_openings",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_job_openings_Status",
                table: "website_job_openings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_website_newsletter_subscribers_ConfirmTokenHash",
                table: "website_newsletter_subscribers",
                column: "ConfirmTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_website_newsletter_subscribers_NormalizedEmail",
                table: "website_newsletter_subscribers",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_newsletter_subscribers_Status_CreatedAt",
                table: "website_newsletter_subscribers",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_website_newsletter_subscribers_UnsubscribeTokenHash",
                table: "website_newsletter_subscribers",
                column: "UnsubscribeTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_pages_IsPublished_Kind",
                table: "website_pages",
                columns: new[] { "IsPublished", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_website_pages_Slug",
                table: "website_pages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_service_categories_IsPublished_SortOrder",
                table: "website_service_categories",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_service_categories_Slug",
                table: "website_service_categories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_service_packages_ServiceId_SortOrder",
                table: "website_service_packages",
                columns: new[] { "ServiceId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_services_CategoryId_SortOrder",
                table: "website_services",
                columns: new[] { "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_services_IsPublished",
                table: "website_services",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_website_services_Slug",
                table: "website_services",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_settings_Key",
                table: "website_settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_team_members_IsPublished_SortOrder",
                table: "website_team_members",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_team_members_Slug",
                table: "website_team_members",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_testimonials_IsPublished_SortOrder",
                table: "website_testimonials",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_testimonials_ServiceId",
                table: "website_testimonials",
                column: "ServiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ads_ads");

            migrationBuilder.DropTable(
                name: "ads_alerts");

            migrationBuilder.DropTable(
                name: "ads_budgets");

            migrationBuilder.DropTable(
                name: "ads_client_settings");

            migrationBuilder.DropTable(
                name: "ads_creatives");

            migrationBuilder.DropTable(
                name: "ads_daily_metrics");

            migrationBuilder.DropTable(
                name: "ads_experiment_variants");

            migrationBuilder.DropTable(
                name: "ads_import_batches");

            migrationBuilder.DropTable(
                name: "ads_media_plan_lines");

            migrationBuilder.DropTable(
                name: "ads_utm_links");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "appeals");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "billing_number_sequences");

            migrationBuilder.DropTable(
                name: "brand_assets");

            migrationBuilder.DropTable(
                name: "brand_kits");

            migrationBuilder.DropTable(
                name: "brief_templates");

            migrationBuilder.DropTable(
                name: "briefs");

            migrationBuilder.DropTable(
                name: "campaign_assets");

            migrationBuilder.DropTable(
                name: "campaign_disclosures");

            migrationBuilder.DropTable(
                name: "campaign_platforms");

            migrationBuilder.DropTable(
                name: "client_feedback");

            migrationBuilder.DropTable(
                name: "client_meetings");

            migrationBuilder.DropTable(
                name: "client_members");

            migrationBuilder.DropTable(
                name: "client_onboarding_items");

            migrationBuilder.DropTable(
                name: "client_reports");

            migrationBuilder.DropTable(
                name: "client_team_assignments");

            migrationBuilder.DropTable(
                name: "content_calendar_entries");

            migrationBuilder.DropTable(
                name: "contract_lines");

            migrationBuilder.DropTable(
                name: "credit_note_applications");

            migrationBuilder.DropTable(
                name: "crm_activities");

            migrationBuilder.DropTable(
                name: "crm_assignment_cursors");

            migrationBuilder.DropTable(
                name: "crm_deal_contacts");

            migrationBuilder.DropTable(
                name: "crm_engagements");

            migrationBuilder.DropTable(
                name: "crm_inbound_events");

            migrationBuilder.DropTable(
                name: "crm_saved_views");

            migrationBuilder.DropTable(
                name: "crm_scoring_rules");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "deliverable_comments");

            migrationBuilder.DropTable(
                name: "deliverable_reviews");

            migrationBuilder.DropTable(
                name: "deliverable_versions");

            migrationBuilder.DropTable(
                name: "delivery_dispatch_keys");

            migrationBuilder.DropTable(
                name: "earning_entries");

            migrationBuilder.DropTable(
                name: "email_automation_step_runs");

            migrationBuilder.DropTable(
                name: "email_automation_steps");

            migrationBuilder.DropTable(
                name: "email_campaign_recipients");

            migrationBuilder.DropTable(
                name: "email_campaign_variants");

            migrationBuilder.DropTable(
                name: "email_consent_records");

            migrationBuilder.DropTable(
                name: "email_events");

            migrationBuilder.DropTable(
                name: "email_imports");

            migrationBuilder.DropTable(
                name: "email_list_memberships");

            migrationBuilder.DropTable(
                name: "email_segments");

            migrationBuilder.DropTable(
                name: "email_sender_profiles");

            migrationBuilder.DropTable(
                name: "email_subscriber_fields");

            migrationBuilder.DropTable(
                name: "email_subscriber_tags");

            migrationBuilder.DropTable(
                name: "email_suppressions");

            migrationBuilder.DropTable(
                name: "email_templates");

            migrationBuilder.DropTable(
                name: "email_tracked_links");

            migrationBuilder.DropTable(
                name: "email_workspace_settings");

            migrationBuilder.DropTable(
                name: "experiment_assignments");

            migrationBuilder.DropTable(
                name: "faq_items");

            migrationBuilder.DropTable(
                name: "form_consent_versions");

            migrationBuilder.DropTable(
                name: "form_email_outbox");

            migrationBuilder.DropTable(
                name: "form_submission_files");

            migrationBuilder.DropTable(
                name: "form_templates");

            migrationBuilder.DropTable(
                name: "homepage_banners");

            migrationBuilder.DropTable(
                name: "hourly_rates");

            migrationBuilder.DropTable(
                name: "integration_connections");

            migrationBuilder.DropTable(
                name: "invitation_links");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "invoice_payments");

            migrationBuilder.DropTable(
                name: "invoice_reminders");

            migrationBuilder.DropTable(
                name: "job_leases");

            migrationBuilder.DropTable(
                name: "job_runs");

            migrationBuilder.DropTable(
                name: "landing_page_assignments");

            migrationBuilder.DropTable(
                name: "landing_page_templates");

            migrationBuilder.DropTable(
                name: "landing_page_versions");

            migrationBuilder.DropTable(
                name: "landing_page_views");

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
                name: "project_members");

            migrationBuilder.DropTable(
                name: "project_templates");

            migrationBuilder.DropTable(
                name: "proposal_lines");

            migrationBuilder.DropTable(
                name: "recurring_task_rules");

            migrationBuilder.DropTable(
                name: "referrals");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "report_templates");

            migrationBuilder.DropTable(
                name: "retention_message_logs");

            migrationBuilder.DropTable(
                name: "reward_rules");

            migrationBuilder.DropTable(
                name: "seo_audit_issues");

            migrationBuilder.DropTable(
                name: "seo_audit_pages");

            migrationBuilder.DropTable(
                name: "seo_audit_rules");

            migrationBuilder.DropTable(
                name: "seo_backlinks");

            migrationBuilder.DropTable(
                name: "seo_citations");

            migrationBuilder.DropTable(
                name: "seo_content_briefs");

            migrationBuilder.DropTable(
                name: "seo_local_profiles");

            migrationBuilder.DropTable(
                name: "seo_outreach_prospects");

            migrationBuilder.DropTable(
                name: "seo_rank_snapshots");

            migrationBuilder.DropTable(
                name: "seo_reviews");

            migrationBuilder.DropTable(
                name: "seo_search_performance");

            migrationBuilder.DropTable(
                name: "sm_awareness_days");

            migrationBuilder.DropTable(
                name: "sm_campaigns");

            migrationBuilder.DropTable(
                name: "sm_caption_snippets");

            migrationBuilder.DropTable(
                name: "sm_client_settings");

            migrationBuilder.DropTable(
                name: "sm_competitor_snapshots");

            migrationBuilder.DropTable(
                name: "sm_hashtag_sets");

            migrationBuilder.DropTable(
                name: "sm_inbox_replies");

            migrationBuilder.DropTable(
                name: "sm_listening_queries");

            migrationBuilder.DropTable(
                name: "sm_media_assets");

            migrationBuilder.DropTable(
                name: "sm_mentions");

            migrationBuilder.DropTable(
                name: "sm_metric_imports");

            migrationBuilder.DropTable(
                name: "sm_network_presets");

            migrationBuilder.DropTable(
                name: "sm_post_comments");

            migrationBuilder.DropTable(
                name: "sm_post_metrics");

            migrationBuilder.DropTable(
                name: "sm_post_variants");

            migrationBuilder.DropTable(
                name: "sm_profile_metrics");

            migrationBuilder.DropTable(
                name: "sm_publish_attempts");

            migrationBuilder.DropTable(
                name: "sm_queue_slots");

            migrationBuilder.DropTable(
                name: "submission_events");

            migrationBuilder.DropTable(
                name: "submission_flags");

            migrationBuilder.DropTable(
                name: "support_messages");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "task_assignees");

            migrationBuilder.DropTable(
                name: "task_attachments");

            migrationBuilder.DropTable(
                name: "task_checklist_items");

            migrationBuilder.DropTable(
                name: "task_comments");

            migrationBuilder.DropTable(
                name: "task_dependencies");

            migrationBuilder.DropTable(
                name: "task_watchers");

            migrationBuilder.DropTable(
                name: "thread_messages");

            migrationBuilder.DropTable(
                name: "thread_read_states");

            migrationBuilder.DropTable(
                name: "time_entries");

            migrationBuilder.DropTable(
                name: "timesheets");

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
                name: "website_blog_categories");

            migrationBuilder.DropTable(
                name: "website_blog_posts");

            migrationBuilder.DropTable(
                name: "website_case_studies");

            migrationBuilder.DropTable(
                name: "website_consultation_blackouts");

            migrationBuilder.DropTable(
                name: "website_consultation_bookings");

            migrationBuilder.DropTable(
                name: "website_consultation_settings");

            migrationBuilder.DropTable(
                name: "website_job_application_notes");

            migrationBuilder.DropTable(
                name: "website_newsletter_subscribers");

            migrationBuilder.DropTable(
                name: "website_pages");

            migrationBuilder.DropTable(
                name: "website_service_packages");

            migrationBuilder.DropTable(
                name: "website_settings");

            migrationBuilder.DropTable(
                name: "website_testimonials");

            migrationBuilder.DropTable(
                name: "ads_ad_groups");

            migrationBuilder.DropTable(
                name: "ads_experiments");

            migrationBuilder.DropTable(
                name: "ads_media_plans");

            migrationBuilder.DropTable(
                name: "post_templates");

            migrationBuilder.DropTable(
                name: "credit_notes");

            migrationBuilder.DropTable(
                name: "deliverables");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "email_automation_enrollments");

            migrationBuilder.DropTable(
                name: "email_campaigns");

            migrationBuilder.DropTable(
                name: "email_lists");

            migrationBuilder.DropTable(
                name: "experiment_variants");

            migrationBuilder.DropTable(
                name: "form_submissions");

            migrationBuilder.DropTable(
                name: "landing_pages");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "onboarding_steps");

            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "proposal_versions");

            migrationBuilder.DropTable(
                name: "tax_rates");

            migrationBuilder.DropTable(
                name: "seo_audits");

            migrationBuilder.DropTable(
                name: "seo_citation_sources");

            migrationBuilder.DropTable(
                name: "seo_keywords");

            migrationBuilder.DropTable(
                name: "sm_competitors");

            migrationBuilder.DropTable(
                name: "sm_inbox_items");

            migrationBuilder.DropTable(
                name: "sm_posts");

            migrationBuilder.DropTable(
                name: "sm_brand_profiles");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropTable(
                name: "delivery_files");

            migrationBuilder.DropTable(
                name: "message_threads");

            migrationBuilder.DropTable(
                name: "tracking_links");

            migrationBuilder.DropTable(
                name: "achievements");

            migrationBuilder.DropTable(
                name: "website_team_members");

            migrationBuilder.DropTable(
                name: "website_industries");

            migrationBuilder.DropTable(
                name: "website_inquiries");

            migrationBuilder.DropTable(
                name: "website_job_applications");

            migrationBuilder.DropTable(
                name: "website_services");

            migrationBuilder.DropTable(
                name: "ads_campaigns");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "project_tasks");

            migrationBuilder.DropTable(
                name: "email_automations");

            migrationBuilder.DropTable(
                name: "email_subscribers");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "forms");

            migrationBuilder.DropTable(
                name: "payout_batches");

            migrationBuilder.DropTable(
                name: "seo_sites");

            migrationBuilder.DropTable(
                name: "reward_rule_sets");

            migrationBuilder.DropTable(
                name: "social_accounts");

            migrationBuilder.DropTable(
                name: "stored_files");

            migrationBuilder.DropTable(
                name: "website_cv_files");

            migrationBuilder.DropTable(
                name: "website_job_openings");

            migrationBuilder.DropTable(
                name: "website_service_categories");

            migrationBuilder.DropTable(
                name: "ads_accounts");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "project_milestones");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "campaign_categories");

            migrationBuilder.DropTable(
                name: "crm_deals");

            migrationBuilder.DropTable(
                name: "crm_contacts");

            migrationBuilder.DropTable(
                name: "crm_pipeline_stages");

            migrationBuilder.DropTable(
                name: "crm_companies");

            migrationBuilder.DropTable(
                name: "client_accounts");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
