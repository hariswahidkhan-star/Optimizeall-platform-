using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Common.Errors;

/// <summary>
/// Maps exceptions to RFC 7807 problem responses. Business errors keep their code/message; unexpected errors
/// are logged with the trace id and returned without internal details.
/// </summary>
public sealed class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> logger, IProblemDetailsService problems)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title, errors) = exception switch
        {
            DomainException de => (StatusFor(de.Kind), de.Code, de.Message, de.Errors),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency.conflict",
                "This record was changed by someone else. Reload and try again.", null),
            DbUpdateException due when IsUniqueViolation(due) => (StatusCodes.Status409Conflict, "db.duplicate",
                "A record with the same unique value already exists.", null),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                (499, "request.cancelled", "The request was cancelled.", null),
            BadHttpRequestException bre => (bre.StatusCode, "request.invalid", "The request could not be read.", null),
            _ => (StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred.", (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path} (trace {TraceId})",
                httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);
        else if (status == StatusCodes.Status409Conflict)
            logger.LogInformation("Conflict {Code} for {Method} {Path}: {Message}", code, httpContext.Request.Method,
                httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://docs.optimizeall.app/errors/{code}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors;

        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem, Exception = exception });
    }

    private static int StatusFor(DomainErrorKind kind) => kind switch
    {
        DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
        DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
        DomainErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        DomainErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        DomainErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status400BadRequest,
    };

    /// <summary>Unique/primary-key violation on either provider (MySQL ER_DUP_ENTRY, SQLite constraint 2067/1555).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex) => Persistence.DatabaseErrors.IsUniqueViolation(ex);
}
