using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Files;

public enum FilePurpose
{
    /// <summary>Private: only the owner and staff with submissions.review may read it.</summary>
    SubmissionScreenshot,
    /// <summary>Public campaign creative.</summary>
    CampaignAsset,
    ContentImage,
    /// <summary>Private proof of a discount-code sale (receipt/screenshot): the owner and staff with sales.review or codes.view.</summary>
    SaleProof,
}

/// <summary>Metadata of an uploaded file. Bytes live in private storage keyed by <see cref="StorageKey"/>, never under the web root.</summary>
public class StoredFile : Entity
{
    public Guid OwnerUserId { get; set; }
    public FilePurpose Purpose { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsPublic { get; set; }
}
