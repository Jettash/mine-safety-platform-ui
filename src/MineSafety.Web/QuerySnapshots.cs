namespace MineSafety.Web;

public sealed record SavedQuery(string Id, string Owner, QueryScope Scope, DateTimeOffset ExpiresAt,
    IReadOnlyList<RecordRow> Records, RecordSummary Summary);

public sealed class QuerySnapshots(WebOptions options, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<string, SavedQuery> queries = new(StringComparer.Ordinal);
    private long cachedRows;

    public SavedQuery Add(string owner, QueryScope scope, IReadOnlyList<RecordRow> rows)
    {
        var query = new SavedQuery(Guid.NewGuid().ToString("N"), owner, scope,
            clock.GetUtcNow().AddMinutes(options.SnapshotMinutes), rows, RecordSummary.From(rows));
        lock (gate)
        {
            RemoveExpired();
            // Include empty results in the budget so requests cannot create unbounded entries.
            var cost = Math.Max(1, rows.Count);
            if (cachedRows + cost > options.MaxCachedRecords)
                throw new ApiError(503, "QUERY_CACHE_FULL", "查询缓存已满，请稍后重试或缩小查询范围。");
            queries.Add(query.Id, query);
            cachedRows += cost;
        }
        return query;
    }
    public SavedQuery Get(string id, string owner)
    {
        lock (gate)
        {
            RemoveExpired();
            if (!queries.TryGetValue(id, out var value) || value.Owner != owner)
                throw new ApiError(410, "QUERY_EXPIRED", "本次查询已过期，请重新点击查询。");
            return value;
        }
    }
    private void RemoveExpired()
    {
        foreach (var item in queries.Values.Where(q => q.ExpiresAt <= clock.GetUtcNow()).ToArray())
        {
            queries.Remove(item.Id);
            cachedRows -= Math.Max(1, item.Records.Count);
        }
    }
    public static object Page(SavedQuery query, int page, int size)
    {
        if (page < 1 || size is not (10 or 20 or 50))
            throw new ApiError(400, "INVALID_PAGE", "分页参数无效。");
        var pages = Math.Max(1, (int)Math.Ceiling(query.Records.Count / (double)size));
        page = Math.Min(page, pages);
        return new
        {
            snapshotId = query.Id,
            query.Scope,
            query.ExpiresAt,
            query.Summary,
            totalCount = query.Records.Count,
            page,
            pageSize = size,
            pages,
            items = query.Records.Skip((page - 1) * size).Take(size).ToArray()
        };
    }
}
