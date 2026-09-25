namespace OptimizeAll.Domain.Common;

/// <summary>
/// A business-rule violation. <see cref="Code"/> is a stable machine-readable identifier returned to clients
/// (e.g. "submission.duplicate_url"); the API maps <see cref="Kind"/> to an HTTP status.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }
    public DomainErrorKind Kind { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public DomainException(string code, string message, DomainErrorKind kind = DomainErrorKind.Validation,
        IReadOnlyDictionary<string, string[]>? errors = null) : base(message)
    {
        Code = code;
        Kind = kind;
        Errors = errors;
    }

    public static DomainException NotFound(string what) =>
        new($"{what.ToLowerInvariant()}.not_found", $"{what} was not found.", DomainErrorKind.NotFound);

    public static DomainException Conflict(string code, string message) => new(code, message, DomainErrorKind.Conflict);

    public static DomainException Forbidden(string code, string message) => new(code, message, DomainErrorKind.Forbidden);

    /// <summary>A well-formed request the server will not carry out as asked (422), e.g. an export over its row cap.</summary>
    public static DomainException Unprocessable(string code, string message) => new(code, message, DomainErrorKind.Unprocessable);
}

public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    TooManyRequests,
    Unprocessable,
}
