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
                    ImpersonatorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                name: "billing_number_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LastValue = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_number_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "brief_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ServiceLine = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Fields = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_templates", x => x.Id);
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
                name: "content_copy_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_copy_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_assignment_cursors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pool = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    LastUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_assignment_cursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_inbound_events",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DealId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_inbound_events", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "crm_pipeline_stages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    WinProbability = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_pipeline_stages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_proposal_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ProposalTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ValidForDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Goals = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: true),
                    Deliverables = table.Column<string>(type: "TEXT", nullable: true),
                    Timeline = table.Column<string>(type: "TEXT", nullable: true),
                    Terms = table.Column<string>(type: "TEXT", nullable: true),
                    Lines = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_proposal_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "crm_scoring_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    MatchValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOccurrences = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_scoring_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "custom_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Permissions = table.Column<string>(type: "TEXT", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_custom_roles", x => x.Id);
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
                name: "delivery_dispatch_keys",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_dispatch_keys", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "email_template_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    ActionLabel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_template_overrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "email_tracked_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UrlHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_tracked_links", x => x.Id);
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
                name: "form_email_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAddress = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    ToName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_email_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "form_templates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SchemaJson = table.Column<string>(type: "TEXT", nullable: false),
                    SubmitLabel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SuccessMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConsentText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AutoresponderSubject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AutoresponderBody = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustom = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustomized = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_form_templates", x => x.Key);
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
                name: "hourly_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Rate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hourly_rates", x => x.Id);
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
                name: "landing_page_templates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MetaTitle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    BlocksJson = table.Column<string>(type: "TEXT", nullable: false),
                    FormTemplateKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustom = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustomized = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_landing_page_templates", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "learning_courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LatestVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Subtitle = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ModuleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LessonCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BadgeName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Skills = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_courses", x => x.Id);
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
                name: "project_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ProjectType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ServiceLines = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultBudgetHours = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    DurationDays = table.Column<int>(type: "INTEGER", nullable: true),
                    Milestones = table.Column<string>(type: "TEXT", nullable: false),
                    Tasks = table.Column<string>(type: "TEXT", nullable: false),
                    Recurring = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    MembershipMode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AutoTiers = table.Column<string>(type: "TEXT", nullable: false),
                    AutoMinFollowers = table.Column<int>(type: "INTEGER", nullable: true),
                    AutoMaxFollowers = table.Column<int>(type: "INTEGER", nullable: true),
                    AutoRequireVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchiveReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "report_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Sections = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seo_audit_rules",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    WhyItMatters = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    HowToFix = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_audit_rules", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "seo_citation_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Countries = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustom = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seo_citation_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sm_awareness_days",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Day = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Countries = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_awareness_days", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sm_network_presets",
                columns: table => new
                {
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MaxTextLength = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxTitleLength = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxHashtags = table.Column<int>(type: "INTEGER", nullable: false),
                    RecommendedHashtags = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxMentions = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxMedia = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxVideos = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiresMedia = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresVideo = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowsMixedMedia = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinAspectRatio = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    MaxAspectRatio = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    MaxVideoSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    MinVideoSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxImageBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    MaxAltTextLength = table.Column<int>(type: "INTEGER", nullable: false),
                    SupportsFirstComment = table.Column<bool>(type: "INTEGER", nullable: false),
                    LinkHandling = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RecommendedTimes = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sm_network_presets", x => x.Network);
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
                name: "tax_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RatePercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Inclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    NeedsReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_rates", x => x.Id);
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
                    PermissionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsTestAccount = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_blog_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_blog_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_consultation_blackouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_consultation_blackouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_consultation_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SlotMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MinNoticeHours = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDaysAhead = table.Column<int>(type: "INTEGER", nullable: false),
                    WeeklyAvailability = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_consultation_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_cv_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_cv_files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_industries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    Challenges = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceIds = table.Column<string>(type: "TEXT", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    HeroImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_industries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_job_openings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Department = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    Workplace = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmploymentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DescriptionMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    Requirements = table.Column<string>(type: "TEXT", nullable: false),
                    Benefits = table.Column<string>(type: "TEXT", nullable: false),
                    SalaryMin = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    SalaryMax = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    SalaryCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: true),
                    SalaryPeriod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_job_openings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_newsletter_subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConfirmTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConfirmTokenExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    UnsubscribeTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConsentVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    UnsubscribedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_newsletter_subscribers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BlocksJson = table.Column<string>(type: "TEXT", nullable: false),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Tagline = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    DescriptionMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    RelationshipLabel = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Highlights = table.Column<string>(type: "TEXT", nullable: false),
                    Offerings = table.Column<string>(type: "TEXT", nullable: false),
                    Keywords = table.Column<string>(type: "TEXT", nullable: false),
                    Categories = table.Column<string>(type: "TEXT", nullable: false),
                    SameAs = table.Column<string>(type: "TEXT", nullable: false),
                    RelatedPartnerIds = table.Column<string>(type: "TEXT", nullable: false),
                    Slots = table.Column<string>(type: "TEXT", nullable: false),
                    BrandColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OfferText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    OfferCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    OfferExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    OfferConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    OfferUpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_redirects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ToPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ContentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_redirects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_service_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_service_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Json = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_team_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Bio = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PhotoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Expertise = table.Column<string>(type: "TEXT", nullable: false),
                    SocialLinks = table.Column<string>(type: "TEXT", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_team_members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "website_used_form_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_used_form_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "learning_course_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PackVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    BasedOnVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_course_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_course_versions_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_catalog_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Recurrence = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_catalog_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_catalog_items_tax_rates_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "tax_rates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
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
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    AccountManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LogoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    BillingContactName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BillingEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    BillingAddress = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TaxId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CrmCompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovalSlaDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoApproveAfterDays = table.Column<int>(type: "INTEGER", nullable: true),
                    LastInvoicePaidAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
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
                name: "crm_saved_views",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Entity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FiltersJson = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Shared = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "external_logins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_logins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_logins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "impersonation_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImpersonatorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    ImpersonatorSecurityVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedReason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impersonation_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_impersonation_sessions_users_ImpersonatorUserId",
                        column: x => x.ImpersonatorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_impersonation_sessions_users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "learning_certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ActiveKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    HolderName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CourseSlug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CourseTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BadgeName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Skills = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecipientSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_certificates_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_certificates_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "learning_enrolments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    LastLessonSlug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LessonsCompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PassedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    BestScore = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_enrolments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_enrolments_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learning_enrolments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "rate_cards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ArchivedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchiveReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_cards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_cards_users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_group_member_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_group_member_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_group_member_events_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_group_member_events_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_group_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_group_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_group_members_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_group_members_users_UserId",
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
                name: "timesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionComment = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TotalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                name: "user_custom_roles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomRoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_custom_roles", x => new { x.UserId, x.CustomRoleId });
                    table.ForeignKey(
                        name: "FK_user_custom_roles_custom_roles_CustomRoleId",
                        column: x => x.CustomRoleId,
                        principalTable: "custom_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_custom_roles_users_UserId",
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
                name: "website_inquiries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Company = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceSlugs = table.Column<string>(type: "TEXT", nullable: false),
                    PackageIds = table.Column<string>(type: "TEXT", nullable: false),
                    BudgetRange = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Timeline = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Referrer = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LandingPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConsentVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StaffNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_case_studies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ClientAnonymized = table.Column<bool>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IndustryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceIds = table.Column<string>(type: "TEXT", nullable: false),
                    ChallengeMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    StrategyMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    Metrics = table.Column<string>(type: "TEXT", nullable: false),
                    TestimonialQuote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TestimonialAuthor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    TestimonialRole = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CoverImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    GalleryImageUrls = table.Column<string>(type: "TEXT", nullable: false),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_job_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobOpeningId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    PortfolioUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CoverLetter = table.Column<string>(type: "TEXT", nullable: true),
                    CvFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ConsentVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_page_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BlocksJson = table.Column<string>(type: "TEXT", nullable: false),
                    SeoJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_page_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_page_revisions_website_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "website_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "website_partner_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PagePath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Impressions = table.Column<int>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_website_partner_stats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_website_partner_stats_website_partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "website_partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "website_services",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tagline = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HeroTitle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    HeroBody = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    OverviewMarkdown = table.Column<string>(type: "TEXT", nullable: true),
                    ProblemsSolved = table.Column<string>(type: "TEXT", nullable: false),
                    Deliverables = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessSteps = table.Column<string>(type: "TEXT", nullable: false),
                    Tools = table.Column<string>(type: "TEXT", nullable: false),
                    Kpis = table.Column<string>(type: "TEXT", nullable: false),
                    Faqs = table.Column<string>(type: "TEXT", nullable: false),
                    RelatedServiceIds = table.Column<string>(type: "TEXT", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    HeroImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CtaLabel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    CtaUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_blog_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    LastLiveSlug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    Excerpt = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CoverImageAlt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryIds = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    ReadingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PublishAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RelatedPostIds = table.Column<string>(type: "TEXT", nullable: false),
                    SeoTitle = table.Column<string>(type: "TEXT", maxLength: 70, nullable: true),
                    SeoDescription = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SeoOgImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoCanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SeoNoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                    PersonalRatesMode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PersonalRateMaxMultiplier = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
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
                name: "ads_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastSyncMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BudgetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EvaluatedFor = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_budgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    OverPacingThreshold = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    UnderPacingThreshold = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TargetCpa = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    TargetRoas = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_client_settings",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignNamingTemplate = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DefaultUtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DefaultUtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LowercaseUtm = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_creatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Headlines = table.Column<string>(type: "TEXT", nullable: false),
                    Descriptions = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CallToAction = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    FinalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MediaAssetIds = table.Column<string>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Hypothesis = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Metric = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    WinnerVariant = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EnteredPValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_media_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_utm_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Medium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Campaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    TaggedUrl = table.Column<string>(type: "TEXT", maxLength: 3000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "brand_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "brand_kits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Colors = table.Column<string>(type: "TEXT", nullable: false),
                    Fonts = table.Column<string>(type: "TEXT", nullable: false),
                    ToneOfVoice = table.Column<string>(type: "TEXT", nullable: true),
                    Personas = table.Column<string>(type: "TEXT", nullable: false),
                    Competitors = table.Column<string>(type: "TEXT", nullable: false),
                    Dos = table.Column<string>(type: "TEXT", nullable: false),
                    Donts = table.Column<string>(type: "TEXT", nullable: false),
                    KeyMessages = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Answers = table.Column<string>(type: "TEXT", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedByClient = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConvertedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    StaffNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "client_feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DeliverableId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Period = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "client_meetings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Agenda = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AttendeeUserIds = table.Column<string>(type: "TEXT", nullable: false),
                    ActionItems = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                name: "client_onboarding_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedOnBehalfOfClient = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "client_reminder_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    OffsetsDays = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_reminder_policies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_client_reminder_policies_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_client_reminder_policies_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "client_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Sections = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutoKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "client_team_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceRole = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "code_programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    BrandName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Terms = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StoreUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DiscountLabel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    StartsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EndsAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PayoutType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FlatAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true),
                    DailyCapPerPerson = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    ProgramCapPerPerson = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    MaxOrderAgeDays = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireProof = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayoutVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_programs", x => x.Id);
                    table.CheckConstraint("ck_code_programs_flat", "CAST(\"FlatAmount\" AS REAL) IS NULL OR CAST(\"FlatAmount\" AS REAL) > 0");
                    table.CheckConstraint("ck_code_programs_percent", "CAST(\"Percent\" AS REAL) IS NULL OR (CAST(\"Percent\" AS REAL) > 0 AND CAST(\"Percent\" AS REAL) <= 100)");
                    table.ForeignKey(
                        name: "FK_code_programs_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_programs_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crm_companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Size = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    TagIndex = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CustomFieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "delivery_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_automations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TriggerConfigJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Reentry = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ReentryCooldownDays = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SenderProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EntryStepKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    LastAnniversaryScan = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SegmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SenderProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PreviewText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DesignJson = table.Column<string>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    SmsBody = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: true),
                    WhatsAppTemplateName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    WhatsAppTemplateLanguage = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    WhatsAppParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    ScheduleMode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ScheduledLocalTime = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SendWindowStartHour = table.Column<int>(type: "INTEGER", nullable: true),
                    SendWindowEndHour = table.Column<int>(type: "INTEGER", nullable: true),
                    ThrottlePerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    AbTestPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    AbWinnerMetric = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AbWaitHours = table.Column<int>(type: "INTEGER", nullable: false),
                    AbWinnerVariant = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    AbTestCompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    AbDecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ApprovalDecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovalDecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovalNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SendConfirmedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SendConfirmedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SendStartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ExpandedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PausedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PauseReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RecipientCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_lists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DoubleOptIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowInPreferenceCenter = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConsentTextVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LastCountedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_sender_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    FromName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FromEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    ReplyTo = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VerificationCodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VerificationSentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    VerificationAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmailConsent = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EmailConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SmsConsent = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SmsConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    WhatsAppConsent = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    WhatsAppConsentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastOpenAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastClickAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SoftBounceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_suppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PreviewText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DesignJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_workspace_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    OrganizationName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PhysicalAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    RequireClientApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailProvider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultThrottlePerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    QuietHoursStart = table.Column<int>(type: "INTEGER", nullable: false),
                    QuietHoursEnd = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultTimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SmsCostPerSegment = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    WhatsAppCostPerMessage = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CostCurrency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "forms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SchemaJson = table.Column<string>(type: "TEXT", nullable: false),
                    SubmitLabel = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SuccessMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RedirectUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NotifyUserIds = table.Column<string>(type: "TEXT", nullable: false),
                    AutoresponderEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoresponderSubject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AutoresponderBody = table.Column<string>(type: "TEXT", nullable: true),
                    AllowedOrigins = table.Column<string>(type: "TEXT", nullable: false),
                    ConsentText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ConsentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Captcha = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MinFillSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "integration_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActiveScopeKey = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedSecrets = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastVerifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "landing_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MetaTitle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    OgImageUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NoIndex = table.Column<bool>(type: "INTEGER", nullable: false),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    VariantsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExperimentEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentStartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishedVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    HasUnpublishedChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "message_threads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMessagePreview = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastAuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsInternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ServiceLines = table.Column<string>(type: "TEXT", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    BudgetHours = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    DefaultHourlyRate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    SitemapUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    TargetCountry = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Competitors = table.Column<string>(type: "TEXT", nullable: false),
                    MaxPages = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_brand_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProfileUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ConnectionStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_caption_snippets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_client_settings",
                columns: table => new
                {
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequireClientApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultUtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_competitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Handle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    ProfileUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_hashtag_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Hashtags = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_inbox_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AuthorHandle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AssignedToUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Sentiment = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_listening_queries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Networks = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExternalUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_mentions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AuthorHandle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Sentiment = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SentimentSource = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    SentimentScore = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_metric_imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RowsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsImported = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutoAppendUtm = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEvergreen = table.Column<bool>(type: "INTEGER", nullable: false),
                    EvergreenIntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    EvergreenMaxRepeats = table.Column<int>(type: "INTEGER", nullable: false),
                    EvergreenRepeatCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RecycledFromPostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecycleNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishClaimId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PublishClaimedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "learning_exam_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrolmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActiveKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    QuestionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PassingScore = table.Column<int>(type: "INTEGER", nullable: false),
                    DrawJson = table.Column<string>(type: "TEXT", nullable: false),
                    AnswersJson = table.Column<string>(type: "TEXT", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_exam_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_exam_attempts_learning_course_versions_CourseVersionId",
                        column: x => x.CourseVersionId,
                        principalTable: "learning_course_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_exam_attempts_learning_enrolments_EnrolmentId",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_knowledge_check_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrolmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LessonSlug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    QuestionIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Selected = table.Column<string>(type: "TEXT", nullable: false),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_knowledge_check_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_knowledge_check_answers_learning_enrolments_EnrolmentId",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_lesson_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrolmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LessonSlug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_lesson_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_lesson_progress_learning_enrolments_EnrolmentId",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
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
                name: "rate_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsCustom = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ValidTo = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_assignments", x => x.Id);
                    table.CheckConstraint("ck_rate_assignments_target", "(\"Target\" = 'Person' AND \"UserId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"Target\" = 'Group' AND \"GroupId\" IS NOT NULL AND \"UserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_rate_assignments_campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_assignments_rate_cards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "rate_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_assignments_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rate_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    DailyCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    WeeklyCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    CampaignCapPerParticipant = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    StackCampaignBonuses = table.Column<bool>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MaxIncreasePercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_card_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_card_versions_rate_cards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "rate_cards",
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
                name: "website_consultation_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SlotStart = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SlotEnd = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SlotKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Company = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ServiceSlugs = table.Column<string>(type: "TEXT", nullable: false),
                    VisitorTimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InquiryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_job_application_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    StageFrom = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    StageTo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                        name: "FK_website_job_application_notes_website_job_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "website_job_applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "website_service_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    BillingPeriod = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SetupFee = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Features = table.Column<string>(type: "TEXT", nullable: false),
                    IsMostPopular = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCustomQuote = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "website_testimonials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AuthorRole = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Company = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ServiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                    Format = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
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
                name: "ads_campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BudgetType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    BidStrategy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TargetingSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TargetCpa = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    TargetRoas = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    NamingCompliant = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_daily_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    EntityKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AdId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Spend = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Impressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Conversions = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ConversionValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reach = table.Column<long>(type: "INTEGER", nullable: true),
                    VideoViews = table.Column<long>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_import_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Template = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RowsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsImported = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    RowsSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ToDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_experiment_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsControl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Impressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Conversions = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Spend = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_media_plan_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Objective = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PlannedBudget = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    FlightStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FlightEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    KpiName = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    KpiTarget = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    PlannedImpressions = table.Column<long>(type: "INTEGER", nullable: true),
                    PlannedClicks = table.Column<long>(type: "INTEGER", nullable: true),
                    PlannedConversions = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "code_import_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Matched = table.Column<int>(type: "INTEGER", nullable: false),
                    Flagged = table.Column<int>(type: "INTEGER", nullable: false),
                    Rejected = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_import_batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_code_import_batches_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "code_payout_overrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayoutType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FlatAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_payout_overrides", x => x.Id);
                    table.CheckConstraint("ck_code_payout_overrides_target", "(\"Target\" = 'Person' AND \"UserId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"Target\" = 'Group' AND \"GroupId\" IS NOT NULL AND \"UserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_code_payout_overrides_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_payout_overrides_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_payout_overrides_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "code_program_tiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThresholdSales = table.Column<int>(type: "INTEGER", nullable: false),
                    FlatAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Percent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 4, nullable: true),
                    BonusAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_program_tiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_code_program_tiers_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "crm_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    JobTitle = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LifecycleStage = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConsentStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConsentChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    TagIndex = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BudgetRange = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    ScoredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    FirstTouchSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstTouchMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    FirstTouchAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastTouchSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTouchMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    LastTouchAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_automation_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutomationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    NextKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    AltNextKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_campaign_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PreviewText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SenderProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DesignJson = table.Column<string>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_imports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CsvContent = table.Column<string>(type: "TEXT", nullable: true),
                    MappingJson = table.Column<string>(type: "TEXT", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ConsentAttestation = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConsentSource = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GrantSmsConsent = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttestedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TotalRows = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessedRows = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_automation_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutomationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Iteration = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CurrentStepKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ExitReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    StepsExecuted = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggerDataJson = table.Column<string>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_campaign_recipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Variant = table.Column<string>(type: "TEXT", maxLength: 2, nullable: true),
                    IsTestCohort = table.Column<bool>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SkipReason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    BounceType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    BouncedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    OpenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MachineOpenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ClickedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ClickCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnsubscribedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ComplainedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConvertedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Segments = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_consent_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConsentTextVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecipientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutomationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutomationStepRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IsMachine = table.Column<bool>(type: "INTEGER", nullable: false),
                    Device = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MailClient = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Value = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: true),
                    ExternalReference = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DedupKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_list_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ListId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    UnsubscribedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConfirmationSentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "email_subscriber_fields",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_subscriber_tags",
                columns: table => new
                {
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "form_consent_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "form_submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LandingPageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VariantKey = table.Column<string>(type: "TEXT", maxLength: 1, nullable: true),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Referrer = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EmbedOrigin = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    IpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConsentVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ConsentGiven = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EventPublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    StatusChangedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "landing_page_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    VariantKey = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "landing_page_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "landing_page_views",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantKey = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    ExperimentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VisitorHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ViewedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IsUnique = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReferrerHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "thread_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ThreadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromClient = table.Column<bool>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    AttachmentFileIds = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "thread_read_states",
                columns: table => new
                {
                    ThreadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastReadAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "project_milestones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "recurring_task_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    DueInDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EstimateHours = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Labels = table.Column<string>(type: "TEXT", nullable: false),
                    ClientVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PagesCrawled = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthScore = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NoticeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RobotsTxtFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    SitemapFound = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MaxPages = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDepth = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_backlinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    LinkHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    AnchorText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Rel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CheckMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_citations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ListingUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ListedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ListedAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ListedPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_content_briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetKeyword = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RelatedKeywords = table.Column<string>(type: "TEXT", nullable: false),
                    Questions = table.Column<string>(type: "TEXT", nullable: false),
                    Outline = table.Column<string>(type: "TEXT", nullable: false),
                    WordCountTarget = table.Column<int>(type: "INTEGER", nullable: false),
                    CompetitorUrls = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_keywords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedKeyword = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Intent = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SearchVolume = table.Column<int>(type: "INTEGER", nullable: true),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IsTracked = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_local_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BusinessName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CompletedChecklist = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_outreach_prospects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProspectUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ContactName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    ContactEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    LastContactedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Responded = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseText = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_search_performance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Page = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RowHash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Clicks = table.Column<int>(type: "INTEGER", nullable: false),
                    Impressions = table.Column<int>(type: "INTEGER", nullable: false),
                    Ctr = table.Column<double>(type: "REAL", nullable: false),
                    Position = table.Column<double>(type: "REAL", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_post_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Impressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Reach = table.Column<long>(type: "INTEGER", nullable: false),
                    Engagements = table.Column<long>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoViews = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_profile_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Followers = table.Column<long>(type: "INTEGER", nullable: false),
                    Impressions = table.Column<long>(type: "INTEGER", nullable: false),
                    Reach = table.Column<long>(type: "INTEGER", nullable: false),
                    Engagements = table.Column<long>(type: "INTEGER", nullable: false),
                    Clicks = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoViews = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_queue_slots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    MinuteOfDay = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_competitor_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Followers = table.Column<long>(type: "INTEGER", nullable: false),
                    EngagementRate = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    PostsLast30Days = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_inbox_replies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SentViaApi = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_post_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsClient = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_post_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MediaIds = table.Column<string>(type: "TEXT", nullable: false),
                    AltTexts = table.Column<string>(type: "TEXT", nullable: false),
                    Link = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FirstComment = table.Column<string>(type: "TEXT", nullable: true),
                    Hashtags = table.Column<string>(type: "TEXT", nullable: false),
                    Mentions = table.Column<string>(type: "TEXT", nullable: false),
                    PublishStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ExternalPostId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PublishedUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PublishedManually = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarkedPublishedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "sm_publish_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VariantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AttemptNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FailureKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ExternalPostId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "learning_exam_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CourseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Selected = table.Column<string>(type: "TEXT", nullable: false),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: false),
                    AttemptPassed = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_exam_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_exam_answers_learning_exam_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "learning_exam_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rate_card_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_card_lines", x => x.Id);
                    table.CheckConstraint("ck_rate_card_lines_amount_nonnegative", "CAST(\"Amount\" AS REAL) >= 0");
                    table.ForeignKey(
                        name: "FK_rate_card_lines_rate_card_versions_VersionId",
                        column: x => x.VersionId,
                        principalTable: "rate_card_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "submission_rates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RateAssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RateCardLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CardName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CardAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CardCurrency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    ExchangeRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    DailyCap = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    WeeklyCap = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    CampaignCap = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    StackCampaignBonuses = table.Column<bool>(type: "INTEGER", nullable: false),
                    LineLabel = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    AssignmentValidTo = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_rates", x => x.Id);
                    table.CheckConstraint("ck_submission_rates_fx_positive", "CAST(\"ExchangeRate\" AS REAL) > 0");
                    table.ForeignKey(
                        name: "FK_submission_rates_rate_assignments_RateAssignmentId",
                        column: x => x.RateAssignmentId,
                        principalTable: "rate_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_rates_rate_cards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "rate_cards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_rates_rate_groups_RateGroupId",
                        column: x => x.RateGroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_rates_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ads_ad_groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    BidStrategy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TargetingSummary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "discount_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ValidTo = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discount_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discount_codes_code_import_batches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "code_import_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discount_codes_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crm_deals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrimaryContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Value = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ExpectedCloseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ServiceSlugs = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceDetail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BudgetRange = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    FirstTouchSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstTouchMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FirstTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    FirstTouchAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastTouchSource = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTouchMedium = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastTouchCampaign = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    LastTouchAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LostReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StageChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "crm_engagements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "email_automation_step_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutomationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubscriberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StepType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    ProviderMessageId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Segments = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_automation_step_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_automation_step_runs_email_automation_enrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "email_automation_enrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "form_submission_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FieldKey = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "project_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MilestoneId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EstimateHours = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    Labels = table.Column<string>(type: "TEXT", nullable: false),
                    ClientVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<double>(type: "REAL", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecurrenceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    BriefId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MeetingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_audit_issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AffectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AffectedUrls = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    StatusNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StatusChangedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    StatusChangedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_audit_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseTimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    H1Count = table.Column<int>(type: "INTEGER", nullable: false),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Canonical = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsNoindex = table.Column<bool>(type: "INTEGER", nullable: false),
                    InSitemap = table.Column<bool>(type: "INTEGER", nullable: false),
                    InboundLinks = table.Column<int>(type: "INTEGER", nullable: false),
                    RedirectChain = table.Column<string>(type: "TEXT", nullable: true),
                    FetchError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "seo_rank_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeywordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SiteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SerpFeatures = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ads_ads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AdGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreativeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "discount_code_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ValidTo = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discount_code_assignments", x => x.Id);
                    table.CheckConstraint("ck_discount_code_assignments_target", "(\"Target\" = 'Person' AND \"UserId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"Target\" = 'Group' AND \"GroupId\" IS NOT NULL AND \"UserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_discount_code_assignments_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discount_code_assignments_discount_codes_CodeId",
                        column: x => x.CodeId,
                        principalTable: "discount_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discount_code_assignments_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_discount_code_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "crm_activities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DealId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccursAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RemindAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ReminderSentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    OverdueNotifiedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "crm_deal_contacts",
                columns: table => new
                {
                    DealId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DealId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SentVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RecipientName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    RecipientEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    InvoiceOnAcceptance = table.Column<bool>(type: "INTEGER", nullable: true),
                    ShareTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ShareTokenProtected = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    SentByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstViewedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    LastViewedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    AcceptedVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    SignerName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SignerTitle = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SignerEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    SignerIpHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SignerUserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeclinedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DeclineReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "deliverables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SentToClientAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ClientDueAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ApprovedVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AutoApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "task_assignees",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "task_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "task_checklist_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "task_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    MentionedUserIds = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EditedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "task_dependencies",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockedByTaskId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "task_watchers",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Billable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RunningUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "code_sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProgramId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrderReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedOrderReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActiveOrderKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OrderDate = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    NetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    ExchangeRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProgramNetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ProgramDiscountAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    ProductNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ProofFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    EstimatedCommission = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecisionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CommissionAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    PayoutSourceLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AppliedCaps = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PayoutVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    Verification = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    VerificationNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ReportedNetAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: true),
                    ReportedOrderDate = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ImportBatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    RefundReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_sales", x => x.Id);
                    table.CheckConstraint("ck_code_sales_amounts", "CAST(\"NetAmount\" AS REAL) > 0 AND CAST(\"DiscountAmount\" AS REAL) >= 0 AND CAST(\"ExchangeRate\" AS REAL) > 0");
                    table.ForeignKey(
                        name: "FK_code_sales_code_import_batches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "code_import_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_code_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_discount_code_assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "discount_code_assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_discount_codes_CodeId",
                        column: x => x.CodeId,
                        principalTable: "discount_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_rate_groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "rate_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_stored_files_ProofFileId",
                        column: x => x.ProofFileId,
                        principalTable: "stored_files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_code_sales_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    BillingFrequency = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AutoRenew = table.Column<bool>(type: "INTEGER", nullable: false),
                    RenewalTermMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    NoticePeriodDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentTermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoIssueInvoices = table.Column<bool>(type: "INTEGER", nullable: true),
                    NextPeriodIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProposalVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "proposal_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Goals = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: true),
                    Deliverables = table.Column<string>(type: "TEXT", nullable: true),
                    Timeline = table.Column<string>(type: "TEXT", nullable: true),
                    Terms = table.Column<string>(type: "TEXT", nullable: true),
                    GrossTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    OneTimeTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    MonthlyRecurringValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    FirstYearValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    Locked = table.Column<bool>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "deliverable_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliverableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromClient = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "deliverable_reviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliverableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "deliverable_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeliverableId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    FileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LinkUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "code_sale_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    At = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_sale_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_code_sale_events_code_sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "code_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                    CodeProgramId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CodeSaleId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                    RateSource = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    RateSourceLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RateCardVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RateGroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RateAssignmentId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                        name: "FK_earning_entries_code_programs_CodeProgramId",
                        column: x => x.CodeProgramId,
                        principalTable: "code_programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_earning_entries_code_sales_CodeSaleId",
                        column: x => x.CodeSaleId,
                        principalTable: "code_sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "contract_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    TaxPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PaymentTermsDays = table.Column<int>(type: "INTEGER", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    AmountPaid = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    AmountCredited = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    AmountWrittenOff = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Balance = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContractId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProposalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    PublicTokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PublicTokenProtected = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    IssuedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    WrittenOffAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    WrittenOffByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WriteOffReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "proposal_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProposalVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Recurrence = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    TaxPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "credit_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    AmountApplied = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ServiceSlug = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    DiscountType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DiscountValue = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxRateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaxName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    TaxPercent = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxInclusive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "invoice_payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    ActiveReference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ReversalOfPaymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReversalReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReversalKind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false)
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
                        name: "FK_invoice_payments_invoice_payments_ReversalOfPaymentId",
                        column: x => x.ReversalOfPaymentId,
                        principalTable: "invoice_payments",
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
                    table.ForeignKey(
                        name: "FK_invoice_payments_users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_invoice_payments_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "invoice_reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    SentByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                    table.ForeignKey(
                        name: "FK_invoice_reminders_users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "credit_note_applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    AppliedByUserId = table.Column<Guid>(type: "TEXT", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "payment_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 19, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 3, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_claims_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_claims_invoice_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "invoice_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_claims_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_claims_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payment_claims_users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_proofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaymentClaimId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_proofs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_proofs_client_accounts_ClientAccountId",
                        column: x => x.ClientAccountId,
                        principalTable: "client_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_proofs_invoice_payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "invoice_payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_proofs_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_proofs_payment_claims_PaymentClaimId",
                        column: x => x.PaymentClaimId,
                        principalTable: "payment_claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_payment_proofs_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "IX_audit_logs_ImpersonatorUserId",
                table: "audit_logs",
                column: "ImpersonatorUserId");

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
                name: "IX_client_reminder_policies_ClientAccountId",
                table: "client_reminder_policies",
                column: "ClientAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_client_reminder_policies_UpdatedByUserId",
                table: "client_reminder_policies",
                column: "UpdatedByUserId");

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
                name: "IX_code_import_batches_ProgramId_CreatedAt",
                table: "code_import_batches",
                columns: new[] { "ProgramId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_code_payout_overrides_GroupId",
                table: "code_payout_overrides",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_code_payout_overrides_ProgramId_GroupId",
                table: "code_payout_overrides",
                columns: new[] { "ProgramId", "GroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_code_payout_overrides_ProgramId_UserId",
                table: "code_payout_overrides",
                columns: new[] { "ProgramId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_code_payout_overrides_UserId",
                table: "code_payout_overrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_code_program_tiers_ProgramId_ThresholdSales",
                table: "code_program_tiers",
                columns: new[] { "ProgramId", "ThresholdSales" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_code_programs_CampaignId",
                table: "code_programs",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_code_programs_ClientAccountId",
                table: "code_programs",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_code_programs_Status_CreatedAt",
                table: "code_programs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_code_sale_events_SaleId_At",
                table: "code_sale_events",
                columns: new[] { "SaleId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_AssignmentId",
                table: "code_sales",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_CodeId",
                table: "code_sales",
                column: "CodeId");

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_GroupId",
                table: "code_sales",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_ImportBatchId",
                table: "code_sales",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_ProgramId_ActiveOrderKey",
                table: "code_sales",
                columns: new[] { "ProgramId", "ActiveOrderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_ProgramId_NormalizedOrderReference",
                table: "code_sales",
                columns: new[] { "ProgramId", "NormalizedOrderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_ProgramId_Status",
                table: "code_sales",
                columns: new[] { "ProgramId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_ProofFileId",
                table: "code_sales",
                column: "ProofFileId");

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_Status_SubmittedAt",
                table: "code_sales",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_code_sales_UserId_ProgramId",
                table: "code_sales",
                columns: new[] { "UserId", "ProgramId" });

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
                name: "IX_content_copy_entries_Key",
                table: "content_copy_entries",
                column: "Key",
                unique: true);

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
                name: "IX_crm_companies_ArchivedAt",
                table: "crm_companies",
                column: "ArchivedAt");

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
                name: "IX_crm_contacts_ArchivedAt",
                table: "crm_contacts",
                column: "ArchivedAt");

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
                name: "IX_crm_deals_ArchivedAt",
                table: "crm_deals",
                column: "ArchivedAt");

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
                name: "IX_crm_proposal_templates_Name",
                table: "crm_proposal_templates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_proposal_templates_SortOrder",
                table: "crm_proposal_templates",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_crm_saved_views_Entity_OwnerUserId",
                table: "crm_saved_views",
                columns: new[] { "Entity", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_crm_saved_views_OwnerUserId",
                table: "crm_saved_views",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_custom_roles_NormalizedName",
                table: "custom_roles",
                column: "NormalizedName",
                unique: true);

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
                name: "IX_discount_code_assignments_CodeId_EndedAt",
                table: "discount_code_assignments",
                columns: new[] { "CodeId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_discount_code_assignments_GroupId_ProgramId",
                table: "discount_code_assignments",
                columns: new[] { "GroupId", "ProgramId" });

            migrationBuilder.CreateIndex(
                name: "IX_discount_code_assignments_ProgramId",
                table: "discount_code_assignments",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_discount_code_assignments_UserId_ProgramId",
                table: "discount_code_assignments",
                columns: new[] { "UserId", "ProgramId" });

            migrationBuilder.CreateIndex(
                name: "IX_discount_codes_ImportBatchId",
                table: "discount_codes",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_discount_codes_ProgramId_NormalizedCode",
                table: "discount_codes",
                columns: new[] { "ProgramId", "NormalizedCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discount_codes_ProgramId_Status",
                table: "discount_codes",
                columns: new[] { "ProgramId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_CampaignId_Status",
                table: "earning_entries",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_CodeProgramId_UserId",
                table: "earning_entries",
                columns: new[] { "CodeProgramId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_earning_entries_CodeSaleId",
                table: "earning_entries",
                column: "CodeSaleId");

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
                name: "IX_email_automation_enrollments_AutomationId_SubscriberId_Iteration",
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
                name: "IX_email_campaign_recipients_CampaignId_SentAt",
                table: "email_campaign_recipients",
                columns: new[] { "CampaignId", "SentAt" });

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
                name: "IX_email_subscribers_ScopeKey_CreatedAt",
                table: "email_subscribers",
                columns: new[] { "ScopeKey", "CreatedAt" });

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
                name: "IX_email_template_overrides_Key",
                table: "email_template_overrides",
                column: "Key",
                unique: true);

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
                name: "IX_external_logins_Provider_Subject",
                table: "external_logins",
                columns: new[] { "Provider", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_logins_UserId_Provider",
                table: "external_logins",
                columns: new[] { "UserId", "Provider" },
                unique: true);

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
                name: "IX_form_submissions_ClientAccountId_SubmittedAt_LandingPageId",
                table: "form_submissions",
                columns: new[] { "ClientAccountId", "SubmittedAt", "LandingPageId" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_EventPublishedAt",
                table: "form_submissions",
                column: "EventPublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_FormId_Status",
                table: "form_submissions",
                columns: new[] { "FormId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_FormId_SubmittedAt",
                table: "form_submissions",
                columns: new[] { "FormId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_form_submissions_IpHash_SubmittedAt",
                table: "form_submissions",
                columns: new[] { "IpHash", "SubmittedAt" });

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
                name: "IX_impersonation_sessions_ImpersonatorUserId_EndedAt",
                table: "impersonation_sessions",
                columns: new[] { "ImpersonatorUserId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_sessions_TargetUserId",
                table: "impersonation_sessions",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_impersonation_sessions_TokenHash",
                table: "impersonation_sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_ClientAccountId",
                table: "integration_connections",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_integration_connections_Provider_ActiveScopeKey",
                table: "integration_connections",
                columns: new[] { "Provider", "ActiveScopeKey" },
                unique: true);

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
                name: "IX_invoice_payments_InvoiceId_ActiveReference",
                table: "invoice_payments",
                columns: new[] { "InvoiceId", "ActiveReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_InvoiceId_Reference",
                table: "invoice_payments",
                columns: new[] { "InvoiceId", "Reference" });

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
                name: "IX_invoice_payments_ReversalOfPaymentId",
                table: "invoice_payments",
                column: "ReversalOfPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_ReversedByUserId",
                table: "invoice_payments",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payments_UpdatedByUserId",
                table: "invoice_payments",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_reminders_InvoiceId_Kind",
                table: "invoice_reminders",
                columns: new[] { "InvoiceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_reminders_RequestId",
                table: "invoice_reminders",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_reminders_SentByUserId",
                table: "invoice_reminders",
                column: "SentByUserId");

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
                name: "IX_job_runs_JobName_StartedAt",
                table: "job_runs",
                columns: new[] { "JobName", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_job_runs_StartedAt",
                table: "job_runs",
                column: "StartedAt");

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
                name: "IX_landing_page_views_ClientAccountId_ViewedAt_PageId",
                table: "landing_page_views",
                columns: new[] { "ClientAccountId", "ViewedAt", "PageId" });

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_views_PageId_ViewedAt",
                table: "landing_page_views",
                columns: new[] { "PageId", "ViewedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_landing_page_views_PageId_VisitorHash_ViewedAt",
                table: "landing_page_views",
                columns: new[] { "PageId", "VisitorHash", "ViewedAt" });

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
                name: "IX_learning_certificates_ActiveKey",
                table: "learning_certificates",
                column: "ActiveKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_CourseId_IssuedAt",
                table: "learning_certificates",
                columns: new[] { "CourseId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_UserId_IssuedAt",
                table: "learning_certificates",
                columns: new[] { "UserId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_VerificationCode",
                table: "learning_certificates",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_course_versions_CourseId_Number",
                table: "learning_course_versions",
                columns: new[] { "CourseId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_course_versions_CourseId_PackVersion",
                table: "learning_course_versions",
                columns: new[] { "CourseId", "PackVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_courses_Slug",
                table: "learning_courses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_courses_Status_Category_SortOrder",
                table: "learning_courses",
                columns: new[] { "Status", "Category", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_enrolments_CourseId_EnrolledAt",
                table: "learning_enrolments",
                columns: new[] { "CourseId", "EnrolledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_enrolments_UserId_CourseId",
                table: "learning_enrolments",
                columns: new[] { "UserId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_answers_AttemptId_QuestionId",
                table: "learning_exam_answers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_answers_CourseId_QuestionId",
                table: "learning_exam_answers",
                columns: new[] { "CourseId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_ActiveKey",
                table: "learning_exam_attempts",
                column: "ActiveKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_CourseId_Status_SubmittedAt",
                table: "learning_exam_attempts",
                columns: new[] { "CourseId", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_CourseVersionId",
                table: "learning_exam_attempts",
                column: "CourseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_EnrolmentId",
                table: "learning_exam_attempts",
                column: "EnrolmentId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_UserId_CourseId_StartedAt",
                table: "learning_exam_attempts",
                columns: new[] { "UserId", "CourseId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_knowledge_check_answers_EnrolmentId_LessonSlug_QuestionIndex",
                table: "learning_knowledge_check_answers",
                columns: new[] { "EnrolmentId", "LessonSlug", "QuestionIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_lesson_progress_EnrolmentId_LessonSlug",
                table: "learning_lesson_progress",
                columns: new[] { "EnrolmentId", "LessonSlug" },
                unique: true);

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
                name: "IX_notifications_Type_CreatedAt",
                table: "notifications",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "CreatedAt" });

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
                name: "IX_payment_claims_ClientAccountId",
                table: "payment_claims",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_InvoiceId_Status",
                table: "payment_claims",
                columns: new[] { "InvoiceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_PaymentId",
                table: "payment_claims",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_RequestId",
                table: "payment_claims",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_ReviewedByUserId",
                table: "payment_claims",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_Status_CreatedAt",
                table: "payment_claims",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_claims_SubmittedByUserId",
                table: "payment_claims",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_proofs_ClientAccountId",
                table: "payment_proofs",
                column: "ClientAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_proofs_InvoiceId",
                table: "payment_proofs",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_proofs_PaymentClaimId",
                table: "payment_proofs",
                column: "PaymentClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_proofs_PaymentId",
                table: "payment_proofs",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_proofs_UploadedByUserId",
                table: "payment_proofs",
                column: "UploadedByUserId");

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
                name: "IX_rate_assignments_CampaignId",
                table: "rate_assignments",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_assignments_GroupId_CampaignId",
                table: "rate_assignments",
                columns: new[] { "GroupId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_assignments_RateCardId",
                table: "rate_assignments",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_assignments_UserId_CampaignId",
                table: "rate_assignments",
                columns: new[] { "UserId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_card_lines_VersionId",
                table: "rate_card_lines",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_card_versions_RateCardId_Version",
                table: "rate_card_versions",
                columns: new[] { "RateCardId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_cards_Kind_Status_Name",
                table: "rate_cards",
                columns: new[] { "Kind", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_cards_OwnerUserId",
                table: "rate_cards",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_group_member_events_GroupId_At",
                table: "rate_group_member_events",
                columns: new[] { "GroupId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_group_member_events_UserId_At",
                table: "rate_group_member_events",
                columns: new[] { "UserId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_rate_group_members_GroupId_UserId",
                table: "rate_group_members",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_group_members_UserId",
                table: "rate_group_members",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_groups_MembershipMode_ArchivedAt",
                table: "rate_groups",
                columns: new[] { "MembershipMode", "ArchivedAt" });

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
                name: "IX_service_catalog_items_IsActive_SortOrder",
                table: "service_catalog_items",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_service_catalog_items_TaxRateId",
                table: "service_catalog_items",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_sm_awareness_days_Month_Day_Name",
                table: "sm_awareness_days",
                columns: new[] { "Month", "Day", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sm_awareness_days_SeedKey",
                table: "sm_awareness_days",
                column: "SeedKey",
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
                name: "IX_submission_events_ActorUserId_CreatedAt",
                table: "submission_events",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_events_SubmissionId_CreatedAt",
                table: "submission_events",
                columns: new[] { "SubmissionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_flags_SubmissionId_Type",
                table: "submission_flags",
                columns: new[] { "SubmissionId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_submission_rates_RateAssignmentId",
                table: "submission_rates",
                column: "RateAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_rates_RateCardId",
                table: "submission_rates",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_rates_RateGroupId",
                table: "submission_rates",
                column: "RateGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_rates_SubmissionId",
                table: "submission_rates",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_AssignedReviewerId",
                table: "submissions",
                column: "AssignedReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_CampaignId_Status",
                table: "submissions",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ClaimedByUserId_ClaimExpiresAt",
                table: "submissions",
                columns: new[] { "ClaimedByUserId", "ClaimExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ContentHash",
                table: "submissions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_LiveCheckStatus_Status_LiveCheckDueAt",
                table: "submissions",
                columns: new[] { "LiveCheckStatus", "Status", "LiveCheckDueAt" });

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
                name: "IX_submissions_Status_DecidedAt",
                table: "submissions",
                columns: new[] { "Status", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_Status_SubmittedAt",
                table: "submissions",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_SubmittedAt",
                table: "submissions",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_UserId_CampaignId",
                table: "submissions",
                columns: new[] { "UserId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_UserId_SubmittedAt",
                table: "submissions",
                columns: new[] { "UserId", "SubmittedAt" });

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
                name: "IX_thread_messages_ClientAccountId_CreatedAt",
                table: "thread_messages",
                columns: new[] { "ClientAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_thread_messages_ThreadId_CreatedAt",
                table: "thread_messages",
                columns: new[] { "ThreadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_ClientAccountId_Date",
                table: "time_entries",
                columns: new[] { "ClientAccountId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_Date",
                table: "time_entries",
                column: "Date");

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
                name: "IX_tracking_clicks_ClickedAt",
                table: "tracking_clicks",
                column: "ClickedAt");

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
                name: "IX_user_custom_roles_CustomRoleId",
                table: "user_custom_roles",
                column: "CustomRoleId");

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
                name: "IX_users_IsTestAccount",
                table: "users",
                column: "IsTestAccount");

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
                name: "IX_website_page_revisions_PageId_Version",
                table: "website_page_revisions",
                columns: new[] { "PageId", "Version" },
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
                name: "IX_website_partner_stats_Day",
                table: "website_partner_stats",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "IX_website_partner_stats_PartnerId_Slot_PagePath_Day",
                table: "website_partner_stats",
                columns: new[] { "PartnerId", "Slot", "PagePath", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_partners_IsActive_SortOrder",
                table: "website_partners",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_website_partners_Slug",
                table: "website_partners",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_redirects_CreatedAt",
                table: "website_redirects",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_website_redirects_FromPath",
                table: "website_redirects",
                column: "FromPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_website_redirects_ToPath",
                table: "website_redirects",
                column: "ToPath");

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

            migrationBuilder.CreateIndex(
                name: "IX_website_used_form_tokens_ExpiresAt",
                table: "website_used_form_tokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_website_used_form_tokens_TokenHash",
                table: "website_used_form_tokens",
                column: "TokenHash",
                unique: true);
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
                name: "client_reminder_policies");

            migrationBuilder.DropTable(
                name: "client_reports");

            migrationBuilder.DropTable(
                name: "client_team_assignments");

            migrationBuilder.DropTable(
                name: "code_payout_overrides");

            migrationBuilder.DropTable(
                name: "code_program_tiers");

            migrationBuilder.DropTable(
                name: "code_sale_events");

            migrationBuilder.DropTable(
                name: "content_calendar_entries");

            migrationBuilder.DropTable(
                name: "content_copy_entries");

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
                name: "crm_proposal_templates");

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
                name: "email_template_overrides");

            migrationBuilder.DropTable(
                name: "email_templates");

            migrationBuilder.DropTable(
                name: "email_tracked_links");

            migrationBuilder.DropTable(
                name: "email_workspace_settings");

            migrationBuilder.DropTable(
                name: "experiment_assignments");

            migrationBuilder.DropTable(
                name: "external_logins");

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
                name: "impersonation_sessions");

            migrationBuilder.DropTable(
                name: "integration_connections");

            migrationBuilder.DropTable(
                name: "invitation_links");

            migrationBuilder.DropTable(
                name: "invoice_lines");

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
                name: "learning_certificates");

            migrationBuilder.DropTable(
                name: "learning_exam_answers");

            migrationBuilder.DropTable(
                name: "learning_knowledge_check_answers");

            migrationBuilder.DropTable(
                name: "learning_lesson_progress");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "onboarding_step_completions");

            migrationBuilder.DropTable(
                name: "payment_attempts");

            migrationBuilder.DropTable(
                name: "payment_proofs");

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
                name: "rate_card_lines");

            migrationBuilder.DropTable(
                name: "rate_group_member_events");

            migrationBuilder.DropTable(
                name: "rate_group_members");

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
                name: "service_catalog_items");

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
                name: "submission_rates");

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
                name: "user_custom_roles");

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
                name: "website_page_revisions");

            migrationBuilder.DropTable(
                name: "website_partner_stats");

            migrationBuilder.DropTable(
                name: "website_redirects");

            migrationBuilder.DropTable(
                name: "website_service_packages");

            migrationBuilder.DropTable(
                name: "website_settings");

            migrationBuilder.DropTable(
                name: "website_testimonials");

            migrationBuilder.DropTable(
                name: "website_used_form_tokens");

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
                name: "code_sales");

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
                name: "learning_exam_attempts");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "onboarding_steps");

            migrationBuilder.DropTable(
                name: "payment_claims");

            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "proposal_versions");

            migrationBuilder.DropTable(
                name: "rate_card_versions");

            migrationBuilder.DropTable(
                name: "seo_audits");

            migrationBuilder.DropTable(
                name: "seo_citation_sources");

            migrationBuilder.DropTable(
                name: "seo_keywords");

            migrationBuilder.DropTable(
                name: "tax_rates");

            migrationBuilder.DropTable(
                name: "sm_competitors");

            migrationBuilder.DropTable(
                name: "sm_inbox_items");

            migrationBuilder.DropTable(
                name: "sm_posts");

            migrationBuilder.DropTable(
                name: "sm_brand_profiles");

            migrationBuilder.DropTable(
                name: "rate_assignments");

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
                name: "custom_roles");

            migrationBuilder.DropTable(
                name: "website_team_members");

            migrationBuilder.DropTable(
                name: "website_industries");

            migrationBuilder.DropTable(
                name: "website_inquiries");

            migrationBuilder.DropTable(
                name: "website_job_applications");

            migrationBuilder.DropTable(
                name: "website_pages");

            migrationBuilder.DropTable(
                name: "website_partners");

            migrationBuilder.DropTable(
                name: "website_services");

            migrationBuilder.DropTable(
                name: "ads_campaigns");

            migrationBuilder.DropTable(
                name: "project_tasks");

            migrationBuilder.DropTable(
                name: "discount_code_assignments");

            migrationBuilder.DropTable(
                name: "email_automations");

            migrationBuilder.DropTable(
                name: "email_subscribers");

            migrationBuilder.DropTable(
                name: "experiments");

            migrationBuilder.DropTable(
                name: "forms");

            migrationBuilder.DropTable(
                name: "learning_course_versions");

            migrationBuilder.DropTable(
                name: "learning_enrolments");

            migrationBuilder.DropTable(
                name: "invoice_payments");

            migrationBuilder.DropTable(
                name: "payout_batches");

            migrationBuilder.DropTable(
                name: "seo_sites");

            migrationBuilder.DropTable(
                name: "rate_cards");

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
                name: "project_milestones");

            migrationBuilder.DropTable(
                name: "discount_codes");

            migrationBuilder.DropTable(
                name: "rate_groups");

            migrationBuilder.DropTable(
                name: "learning_courses");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "code_import_batches");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "code_programs");

            migrationBuilder.DropTable(
                name: "proposals");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "crm_deals");

            migrationBuilder.DropTable(
                name: "campaign_categories");

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
