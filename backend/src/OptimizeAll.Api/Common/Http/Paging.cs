using System.ComponentModel.DataAnnotations;
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

    public int Skip => (Page - 1) * PageSize;
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

    /// <summary>Escapes LIKE wildcards in user search input.</summary>
    public static string LikePattern(string search) =>
        "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
