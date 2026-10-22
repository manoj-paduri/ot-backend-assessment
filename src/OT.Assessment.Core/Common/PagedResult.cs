namespace OT.Assessment.Core.Common;

/// <summary>A single page of results together with the paging metadata.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Data,
    int Page,
    int PageSize,
    int Total,
    int TotalPages)
{
    public static PagedResult<T> Create(IReadOnlyList<T> data, int page, int pageSize, int total)
    {
        var totalPages = pageSize > 0 ? (int)Math.Ceiling(total / (double)pageSize) : 0;
        return new PagedResult<T>(data, page, pageSize, total, totalPages);
    }
}
