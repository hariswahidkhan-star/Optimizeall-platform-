using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace OptimizeAll.Api.Common.Http;

/// <summary>Standard list query parameters: ?page=1&amp;pageSize=25&amp;search=...&amp;sort=field&amp;desc=true</summary>
public class PageQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 200)]
    public int PageSize { get; set; } = 25;

    [MaxLength(200)]
    public string? Search { get; set; }

    [MaxLength(50)]
    public string? Sort { get; set; }

    public bool Desc { get; set; } = true;

    /// <summary>
    /// Rows to skip. Computed in 64 bits and capped: a far-out page (e.g. page=85899347) must be an empty page, not an
    /// int overflow that wraps to a negative offset and serves the first page again (or a SQL error on MySQL).
    /// </summary>
    public int Skip => PagingExtensions.SkipFor(Page, PageSize);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page.Page, page.PageSize);
    }

    /// <summary>Rows to skip for a 1-based page, capped at <see cref="int.MaxValue"/> instead of overflowing.</summary>
    public static int SkipFor(int page, int pageSize) => (int)Math.Clamp(((long)page - 1) * pageSize, 0, int.MaxValue);

    /// <summary>
    /// Appends a unique key (normally the id) as the last sort key. Without it, rows that tie on the sort key (same
    /// CreatedAt, same name, ...) have no defined order, and MySQL returns them in a different order for each
    /// LIMIT/OFFSET: paging then repeats some rows and never shows others. Orders by the key alone when the query is
    /// not ordered yet.
    /// </summary>
    public static IOrderedQueryable<T> ThenByKey<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, bool descending = false)
    {
        var ordered = query.Expression is MethodCallExpression call && call.Method.DeclaringType == typeof(Queryable) &&
                      call.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending) or nameof(Queryable.ThenBy)
                          or nameof(Queryable.ThenByDescending);
        if (!ordered) return descending ? query.OrderByDescending(key) : query.OrderBy(key);
        var o = (IOrderedQueryable<T>)query;
        return descending ? o.ThenByDescending(key) : o.ThenBy(key);
    }

    /// <summary>Escapes LIKE wildcards in user search input.</summary>
    public static string LikePattern(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
