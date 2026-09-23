using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

public enum JobOpeningStatus
{
    Draft,
    Open,
    Closed,
}

public enum WorkplaceType
{
    OnSite,
    Hybrid,
    Remote,
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    Contract,
    Internship,
    Temporary,
}

public enum SalaryPeriod
{
    Hour,
    Month,
    Year,
}

public class JobOpening : AuditedEntity, IConcurrencyStamped, ISlugged
{
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country of the role (JobPosting schema); null for fully remote roles.</summary>
    public string? CountryCode { get; set; }
    public WorkplaceType Workplace { get; set; } = WorkplaceType.OnSite;
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FullTime;
    public string Summary { get; set; } = string.Empty;

    /// <summary>Sanitized Markdown.</summary>
    public string DescriptionMarkdown { get; set; } = string.Empty;
    public List<string> Requirements { get; set; } = new();
    public List<string> Benefits { get; set; } = new();
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryCurrency { get; set; }
    public SalaryPeriod? SalaryPeriod { get; set; }
    public JobOpeningStatus Status { get; set; } = JobOpeningStatus.Draft;
    public DateTime? PostedAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public enum ApplicationStage
{
    New,
    Screening,
    Interview,
    Offer,
    Hired,
    Rejected,
}

public class JobApplication : AuditedEntity, IConcurrencyStamped
{
    public Guid JobOpeningId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? PortfolioUrl { get; set; }
    public string? CoverLetter { get; set; }
    public Guid CvFileId { get; set; }
    public ApplicationStage Stage { get; set; } = ApplicationStage.New;
    public DateTime ConsentAt { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public string? IpHash { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<JobApplicationNote> Notes { get; set; } = new();
}

/// <summary>An internal note on an application (stage changes are recorded as notes too).</summary>
public class JobApplicationNote : Entity
{
    public Guid ApplicationId { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public ApplicationStage? StageFrom { get; set; }
    public ApplicationStage? StageTo { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// An applicant's CV (PDF only, verified by magic bytes). Kept in the database rather than public file storage: it is
/// personal data only staff with <c>careers.manage</c> may download.
/// </summary>
public class CareerCvFile : Entity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
}
