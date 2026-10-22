using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Common;
using OT.Assessment.Core.Players;

namespace OT.Assessment.Infrastructure.Caching;

/// <summary>
/// Decorator that puts a distributed cache (Redis when configured, otherwise in-process memory) in front of
/// the expensive read. The cache is an optimisation only: if it is unavailable the call silently falls
/// through to the database.
/// </summary>
public sealed class CachedPlayerQueries : IPlayerQueries
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPlayerQueries _inner;
    private readonly IDistributedCache _cache;
    private readonly TimeSpan _topSpendersTtl;
    private readonly ILogger<CachedPlayerQueries> _logger;

    public CachedPlayerQueries(
        IPlayerQueries inner,
        IDistributedCache cache,
        IOptions<CachingOptions> options,
        ILogger<CachedPlayerQueries> logger)
    {
        _inner = inner;
        _cache = cache;
        _topSpendersTtl = TimeSpan.FromSeconds(Math.Max(0, options.Value.TopSpendersTtlSeconds));
        _logger = logger;
    }

    // A player's own history changes with every wager and is cheap to read (index seek), so it is not cached.
    public Task<PagedResult<PlayerWagerDto>> GetPlayerWagersAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken) =>
        _inner.GetPlayerWagersAsync(accountId, page, pageSize, cancellationToken);

    public async Task<IReadOnlyList<TopSpenderDto>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        if (_topSpendersTtl <= TimeSpan.Zero)
        {
            return await _inner.GetTopSpendersAsync(count, cancellationToken).ConfigureAwait(false);
        }

        var key = $"top-spenders:{count}";

        var cached = await TryReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var fresh = await _inner.GetTopSpendersAsync(count, cancellationToken).ConfigureAwait(false);
        await TryWriteAsync(key, fresh, cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    private async Task<IReadOnlyList<TopSpenderDto>?> TryReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
            return bytes is null
                ? null
                : JsonSerializer.Deserialize<List<TopSpenderDto>>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache read failed for {CacheKey}; using the database", key);
            return null;
        }
    }

    private async Task TryWriteAsync(string key, IReadOnlyList<TopSpenderDto> value, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _topSpendersTtl };
            await _cache.SetAsync(key, bytes, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write failed for {CacheKey}", key);
        }
    }
}
