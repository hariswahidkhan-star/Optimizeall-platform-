using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Website.Catalog;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Careers;

public sealed record JobDto(
    Guid Id, string Slug, string Title, string Department, string Location, string? CountryCode, WorkplaceType Workplace, EmploymentType EmploymentType,
    string Summary, string DescriptionMarkdown, IReadOnlyList<string> Requirements, IReadOnlyList<string> Benefits, decimal? SalaryMin,
    decimal? SalaryMax, string? SalaryCurrency, SalaryPeriod? SalaryPeriod, JobOpeningStatus Status, DateTime? PostedAt, DateTime? ClosesAt,
    int ApplicationCount, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class JobInput : StampedInput
{
    [Required, MaxLength(120)] public string Slug { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Department { get; set; } = string.Empty;
    [Required, MaxLength(120)] public string Location { get; set; } = string.Empty;
    [MaxLength(2)] public string? CountryCode { get; set; }
    [Required] public WorkplaceType? Workplace { get; set; }
    [Required] public EmploymentType? EmploymentType { get; set; }
    [Required, MaxLength(500)] public string Summary { get; set; } = string.Empty;
    [Required, MaxLength(30000)] public string DescriptionMarkdown { get; set; } = string.Empty;
    public List<string?>? Requirements { get; set; }
    public List<string?>? Benefits { get; set; }
    [Range(0, 100_000_000)] public decimal? SalaryMin { get; set; }
    [Range(0, 100_000_000)] public decimal? SalaryMax { get; set; }
    [MaxLength(3)] public string? SalaryCurrency { get; set; }
    public SalaryPeriod? SalaryPeriod { get; set; }
    [Required] public JobOpeningStatus? Status { get; set; }
    public DateTime? ClosesAt { get; set; }
}

public sealed record ApplicationSummaryDto(
    Guid Id, Guid JobOpeningId, string JobTitle, string Name, string Email, ApplicationStage Stage, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record ApplicationNoteDto(Guid Id, Guid? AuthorUserId, string Body, ApplicationStage? StageFrom, ApplicationStage? StageTo, DateTime CreatedAt);

public sealed record ApplicationDto(
    Guid Id, Guid JobOpeningId, string JobTitle, string Name, string Email, string? Phone, string? PortfolioUrl, string? CoverLetter,
    ApplicationStage Stage, string CvFileName, long CvSizeBytes, DateTime ConsentAt, string ConsentVersion, IReadOnlyList<ApplicationNoteDto> Notes,
    DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class ApplicationQuery : PageQuery
{
    public Guid? JobOpeningId { get; set; }
    public ApplicationStage? Stage { get; set; }
}

public sealed class MoveApplicationInput : StampedInput
{
    [Required] public ApplicationStage? Stage { get; set; }
    [MaxLength(4000)] public string? Note { get; set; }
}

public sealed class ApplicationNoteInput
{
    [Required, MinLength(1), MaxLength(4000)] public string Body { get; set; } = string.Empty;
}

/// <summary>Multipart form for <c>POST /public/careers/{slug}/applications</c>.</summary>
public sealed class JobApplicationForm : PublicFormInput
{
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;
    [MaxLength(32)] public string? Phone { get; set; }
    [MaxLength(500)] public string? PortfolioUrl { get; set; }
    [MaxLength(5000)] public string? CoverLetter { get; set; }
    public IFormFile? Cv { get; set; }
}

public sealed record ApplicationReceivedDto(string Reference, string Message);

/// <summary>
/// Careers: job openings (<c>careers.manage</c>), public job pages, applications with a PDF-only CV (checked by magic
/// bytes, stored privately in the database) and a hiring pipeline (New → Screening → Interview → Offer → Hired/Rejected)
/// with notes. All staff actions are audited.
/// </summary>
public sealed class CareersService(
    CmsStore store, IAuditLogger audit, ICurrentUser user, FormGuard guard, IPrivacyHasher hasher, PublicSiteService site, TimeProvider clock)
{
    public const long MaxCvBytes = 5 * 1024 * 1024;

    private AppDbContext Db => store.Db;

    // ---------------------------------------------------------------- Staff: jobs

    public async Task<IReadOnlyList<JobDto>> ListJobsAsync(CancellationToken ct)
    {
        var counts = await Db.Set<JobApplication>().AsNoTracking().GroupBy(a => a.JobOpeningId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var jobs = await Db.Set<JobOpening>().AsNoTracking().OrderBy(j => j.Status).ThenByDescending(j => j.PostedAt ?? j.CreatedAt).ToListAsync(ct);
        return jobs.Select(j => ToDto(j, counts.GetValueOrDefault(j.Id))).ToList();
    }

    public async Task<JobDto> GetJobAsync(Guid id, CancellationToken ct)
    {
        var j = await store.FindAsync<JobOpening>(id, ct, true);
        return ToDto(j, await Db.Set<JobApplication>().CountAsync(a => a.JobOpeningId == id, ct));
    }

    public async Task<JobDto> CreateJobAsync(JobInput input, CancellationToken ct)
    {
        var j = new JobOpening();
        Apply(j, input);
        await store.EnsureSlugFreeAsync<JobOpening>(j.Slug, null, ct);
        Db.Add(j);
        audit.Record("careers.job_created", nameof(JobOpening), j.Id, after: new { j.Slug, j.Title, j.Status });
        await store.SaveAsync(ct);
        return ToDto(j, 0);
    }

    public async Task<JobDto> UpdateJobAsync(Guid id, JobInput input, CancellationToken ct)
    {
        var j = await store.FindAsync<JobOpening>(id, ct);
        CmsStore.CheckStamp(Db, j, input.ConcurrencyStamp);
        var before = new { j.Slug, j.Title, j.Status, j.SalaryMin, j.SalaryMax };
        Apply(j, input);
        await store.EnsureSlugFreeAsync<JobOpening>(j.Slug, j.Id, ct);
        audit.Record("careers.job_updated", nameof(JobOpening), j.Id, before, new { j.Slug, j.Title, j.Status, j.SalaryMin, j.SalaryMax });
        await store.SaveAsync(ct);
        return await GetJobAsync(id, ct);
    }

    public async Task DeleteJobAsync(Guid id, CancellationToken ct)
    {
        if (await Db.Set<JobApplication>().AnyAsync(a => a.JobOpeningId == id, ct))
            throw DomainException.Conflict("careers.job_has_applications", "This job has applications. Close it instead of deleting it.");
        await store.DeleteAsync<JobOpening>(id, "careers.job_deleted", j => new { j.Slug, j.Title }, ct);
    }

    private void Apply(JobOpening j, JobInput r)
    {
        var e = new FieldErrors();
        var slug = WebsiteRules.Slug(r.Slug, "slug", e);
        var country = WebsiteRules.Clean(r.CountryCode)?.ToUpperInvariant();
        if (country is not null && !FieldRules.IsCountryCode(country)) e.Add("countryCode", "Use a two-letter country code.");
        if (r.Workplace is not { } workplace || !Enum.IsDefined(workplace)) e.Add("workplace", "Pick on-site, hybrid or remote.");
        if (r.EmploymentType is not { } type || !Enum.IsDefined(type)) e.Add("employmentType", "Pick an employment type.");
        if (r.Status is not { } status || !Enum.IsDefined(status)) e.Add("status", "Pick a status.");
        var currency = WebsiteRules.Clean(r.SalaryCurrency)?.ToUpperInvariant();
        var hasSalary = r.SalaryMin is not null || r.SalaryMax is not null;
        if (hasSalary && !WebsiteRules.IsCurrency(currency)) e.Add("salaryCurrency", "Add the salary currency, e.g. USD.");
        if (hasSalary && r.SalaryPeriod is null) e.Add("salaryPeriod", "Say whether the salary is per hour, month or year.");
        if (r.SalaryMin is { } min && r.SalaryMax is { } max && max < min) e.Add("salaryMax", "The maximum must not be below the minimum.");
        var description = WebsiteRules.Markdown(r.DescriptionMarkdown, "descriptionMarkdown", e, 30000);
        if (description is null) e.Add("descriptionMarkdown", "Describe the role.");
        var requirements = WebsiteRules.Lines(r.Requirements, "requirements", e, 20, 300);
        var benefits = WebsiteRules.Lines(r.Benefits, "benefits", e, 20, 300);
        e.ThrowIfAny();

        j.Slug = slug;
        j.Title = r.Title.Trim();
        j.Department = r.Department.Trim();
        j.Location = r.Location.Trim();
        j.CountryCode = country;
        j.Workplace = r.Workplace!.Value;
        j.EmploymentType = r.EmploymentType!.Value;
        j.Summary = r.Summary.Trim();
        j.DescriptionMarkdown = description!;
        j.Requirements = requirements;
        j.Benefits = benefits;
        j.SalaryCurrency = hasSalary ? currency : null;
        j.SalaryMin = r.SalaryMin is { } smin && hasSalary ? Money.Round(smin, currency!) : null;
        j.SalaryMax = r.SalaryMax is { } smax && hasSalary ? Money.Round(smax, currency!) : null;
        j.SalaryPeriod = hasSalary ? r.SalaryPeriod : null;
        if (r.Status == JobOpeningStatus.Open && j.Status != JobOpeningStatus.Open) j.PostedAt ??= clock.GetUtcNow().UtcDateTime;
        j.Status = r.Status!.Value;
        j.ClosesAt = WebsiteRules.Utc(r.ClosesAt);
    }

    private static JobDto ToDto(JobOpening j, int applications) => new(
        j.Id, j.Slug, j.Title, j.Department, j.Location, j.CountryCode, j.Workplace, j.EmploymentType, j.Summary, j.DescriptionMarkdown,
        j.Requirements, j.Benefits, j.SalaryMin, j.SalaryMax, j.SalaryCurrency, j.SalaryPeriod, j.Status, j.PostedAt, j.ClosesAt, applications,
        j.UpdatedAt, j.ConcurrencyStamp);

    // ---------------------------------------------------------------- Staff: applications

    public async Task<PagedResult<ApplicationSummaryDto>> ListApplicationsAsync(ApplicationQuery query, CancellationToken ct)
    {
        var q = from a in Db.Set<JobApplication>().AsNoTracking()
                join j in Db.Set<JobOpening>() on a.JobOpeningId equals j.Id
                select new { a, j.Title };
        if (query.JobOpeningId is { } jobId) q = q.Where(x => x.a.JobOpeningId == jobId);
        if (query.Stage is { } stage) q = q.Where(x => x.a.Stage == stage);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.a.Name, p) || EF.Functions.Like(x.a.Email, p));
        }
        return await q.OrderByDescending(x => x.a.CreatedAt)
            .Select(x => new ApplicationSummaryDto(x.a.Id, x.a.JobOpeningId, x.Title, x.a.Name, x.a.Email, x.a.Stage, x.a.CreatedAt, x.a.UpdatedAt))
            .ToPagedAsync(query, ct);
    }

    public async Task<ApplicationDto> GetApplicationAsync(Guid id, CancellationToken ct)
    {
        var a = await Db.Set<JobApplication>().AsNoTracking().Include(x => x.Notes).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw CmsStore.NotFound<JobApplication>();
        var title = await Db.Set<JobOpening>().Where(j => j.Id == a.JobOpeningId).Select(j => j.Title).FirstAsync(ct);
        var cv = await Db.Set<CareerCvFile>().AsNoTracking().Where(f => f.Id == a.CvFileId).Select(f => new { f.FileName, f.SizeBytes }).FirstAsync(ct);
        return new ApplicationDto(a.Id, a.JobOpeningId, title, a.Name, a.Email, a.Phone, a.PortfolioUrl, a.CoverLetter, a.Stage, cv.FileName,
            cv.SizeBytes, a.ConsentAt, a.ConsentVersion,
            a.Notes.OrderBy(n => n.CreatedAt).Select(n => new ApplicationNoteDto(n.Id, n.AuthorUserId, n.Body, n.StageFrom, n.StageTo, n.CreatedAt)).ToList(),
            a.CreatedAt, a.UpdatedAt, a.ConcurrencyStamp);
    }

    public async Task<ApplicationDto> MoveAsync(Guid id, MoveApplicationInput input, CancellationToken ct)
    {
        var a = await store.FindAsync<JobApplication>(id, ct);
        CmsStore.CheckStamp(Db, a, input.ConcurrencyStamp);
        if (input.Stage is not { } stage || !Enum.IsDefined(stage)) throw FieldRules.FieldError("careers.invalid", "stage", "Pick a stage.");
        if (stage == a.Stage) return await GetApplicationAsync(id, ct);
        var from = a.Stage;
        a.Stage = stage;
        Db.Add(new JobApplicationNote
        {
            ApplicationId = a.Id, AuthorUserId = user.Id, StageFrom = from, StageTo = stage, CreatedAt = clock.GetUtcNow().UtcDateTime,
            Body = WebsiteRules.Clean(input.Note) ?? $"Moved from {from} to {stage}.",
        });
        audit.Record("careers.application_moved", nameof(JobApplication), a.Id, new { Stage = from }, new { Stage = stage }, WebsiteRules.Clean(input.Note));
        await Db.SaveChangesAsync(ct);
        return await GetApplicationAsync(id, ct);
    }

    public async Task<ApplicationDto> AddNoteAsync(Guid id, ApplicationNoteInput input, CancellationToken ct)
    {
        var a = await store.FindAsync<JobApplication>(id, ct);
        Db.Add(new JobApplicationNote { ApplicationId = a.Id, AuthorUserId = user.Id, Body = input.Body.Trim(), CreatedAt = clock.GetUtcNow().UtcDateTime });
        ConcurrencyGuard.Touch(Db, a);
        audit.Record("careers.application_note_added", nameof(JobApplication), a.Id, after: new { Length = input.Body.Length });
        await Db.SaveChangesAsync(ct);
        return await GetApplicationAsync(id, ct);
    }

    public async Task<CareerCvFile> DownloadCvAsync(Guid applicationId, CancellationToken ct)
    {
        var a = await store.FindAsync<JobApplication>(applicationId, ct, true);
        var file = await Db.Set<CareerCvFile>().AsNoTracking().FirstAsync(f => f.Id == a.CvFileId, ct);
        audit.Record("careers.cv_downloaded", nameof(JobApplication), a.Id);
        await Db.SaveChangesAsync(ct);
        return file;
    }

    // ---------------------------------------------------------------- Public

    private IQueryable<JobOpening> OpenJobs()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return Db.Set<JobOpening>().AsNoTracking().Where(j => j.Status == JobOpeningStatus.Open && (j.ClosesAt == null || j.ClosesAt > now));
    }

    public async Task<IReadOnlyList<JobCardDto>> PublicJobsAsync(CancellationToken ct) =>
        (await OpenJobs().OrderBy(j => j.Department).ThenBy(j => j.Title).ToListAsync(ct))
            .Select(j => new JobCardDto(j.Slug, j.Title, j.Department, j.Location, j.Workplace, j.EmploymentType, j.Summary, j.PostedAt)).ToList();

    public async Task<PublicJobDto> PublicJobAsync(string slug, CancellationToken ct)
    {
        var j = await OpenJobs().FirstOrDefaultAsync(x => x.Slug == slug, ct) ?? throw CmsStore.NotFound<JobOpening>();
        var ld = new JsonLd(await site.BaseUrlAsync(ct), await site.SettingsAsync(ct));
        var path = $"/careers/{j.Slug}";
        return new PublicJobDto(j.Slug, j.Title, j.Department, j.Location, j.Workplace, j.EmploymentType, j.Summary, j.DescriptionMarkdown,
            j.Requirements, j.Benefits,
            j.SalaryCurrency is not null ? new SalaryDto(j.SalaryMin, j.SalaryMax, j.SalaryCurrency, j.SalaryPeriod ?? SalaryPeriod.Year) : null,
            j.PostedAt, j.ClosesAt, new PublicSeoDto($"{j.Title} — Careers", j.Summary, null, ld.Url(path), false),
            new[] { ld.JobPosting(j), ld.Breadcrumbs(("Home", "/"), ("Careers", "/careers"), (j.Title, path)) });
    }

    public async Task<ApplicationReceivedDto> ApplyAsync(string slug, JobApplicationForm form, CancellationToken ct)
    {
        const string message = "Thanks for applying! We review every application and will get back to you within two weeks.";
        if (guard.Check(form, ConsentTexts.CareersVersion) == FormCheck.Spam) return new ApplicationReceivedDto(LeadReference.For(Guid.NewGuid()), message);
        var job = await OpenJobs().FirstOrDefaultAsync(x => x.Slug == slug, ct) ?? throw CmsStore.NotFound<JobOpening>();

        var e = new FieldErrors();
        var email = form.Email.Trim();
        if (!FieldRules.IsEmail(email)) e.Add("email", "Enter a valid email address.");
        var phone = WebsiteRules.Clean(form.Phone);
        if (phone is not null && !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9][0-9 ().-]{5,30}$")) e.Add("phone", "Enter a valid phone number.");
        var portfolio = WebsiteRules.Clean(form.PortfolioUrl);
        if (portfolio is not null && (!portfolio.StartsWith("https://", StringComparison.Ordinal) || !FieldRules.IsSafeContentUrl(portfolio)))
            e.Add("portfolioUrl", "Use an https:// link to your portfolio or profile.");
        var cv = await ReadCvAsync(form.Cv, e, ct);
        e.ThrowIfAny();

        var now = clock.GetUtcNow().UtcDateTime;
        var file = new CareerCvFile
        {
            FileName = FileService.SanitizeFileName(form.Cv!.FileName, ".pdf"),
            SizeBytes = cv!.Length,
            Sha256 = Normalization.Sha256Hex(cv),
            Content = cv,
            CreatedAt = now,
        };
        var application = new JobApplication
        {
            JobOpeningId = job.Id,
            Name = form.Name.Trim(),
            Email = email,
            Phone = phone,
            PortfolioUrl = portfolio,
            CoverLetter = WebsiteRules.Clean(form.CoverLetter),
            CvFileId = file.Id,
            Stage = ApplicationStage.New,
            ConsentAt = now,
            ConsentVersion = form.ConsentVersion,
            IpHash = hasher.Hash(user.IpAddress),
        };
        Db.Add(file);
        Db.Add(application);
        await Db.SaveChangesAsync(ct);
        return new ApplicationReceivedDto(LeadReference.For(application.Id), message);
    }

    /// <summary>Reads the CV and accepts only real PDFs (magic bytes + trailer), whatever the file name or MIME type claims.</summary>
    private static async Task<byte[]?> ReadCvAsync(IFormFile? upload, FieldErrors e, CancellationToken ct)
    {
        if (upload is null || upload.Length == 0)
        {
            e.Add("cv", "Attach your CV as a PDF.");
            return null;
        }
        if (upload.Length > MaxCvBytes)
        {
            e.Add("cv", "Your CV must be 5 MB or smaller.");
            return null;
        }
        using var buffer = new MemoryStream((int)upload.Length);
        await using (var input = upload.OpenReadStream()) await input.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (bytes.Length > MaxCvBytes)
        {
            e.Add("cv", "Your CV must be 5 MB or smaller.");
            return null;
        }
        if (!PdfSignature.IsPdf(bytes))
        {
            e.Add("cv", "Upload your CV as a PDF file.");
            return null;
        }
        return bytes;
    }
}
