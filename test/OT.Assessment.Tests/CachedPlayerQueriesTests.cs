using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OT.Assessment.Infrastructure.Caching;
using OT.Assessment.Tests.Support;

namespace OT.Assessment.Tests;

public class CachedPlayerQueriesTests
{
    private static CachedPlayerQueries Create(FakePlayerQueries inner, IDistributedCache cache, int ttlSeconds) =>
        new(
            inner,
            cache,
            Options.Create(new CachingOptions { TopSpendersTtlSeconds = ttlSeconds }),
            NullLogger<CachedPlayerQueries>.Instance);

    private static IDistributedCache NewMemoryCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task The_second_call_is_served_from_the_cache()
    {
        var inner = new FakePlayerQueries();
        var queries = Create(inner, NewMemoryCache(), ttlSeconds: 60);

        var first = await queries.GetTopSpendersAsync(10, CancellationToken.None);
        var second = await queries.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(1, inner.TopSpenderCalls);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first[0].Username, second[0].Username);
    }

    [Fact]
    public async Task Different_counts_are_cached_separately()
    {
        var inner = new FakePlayerQueries();
        var queries = Create(inner, NewMemoryCache(), ttlSeconds: 60);

        await queries.GetTopSpendersAsync(5, CancellationToken.None);
        await queries.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(2, inner.TopSpenderCalls);
    }

    [Fact]
    public async Task A_zero_ttl_disables_caching()
    {
        var inner = new FakePlayerQueries();
        var queries = Create(inner, NewMemoryCache(), ttlSeconds: 0);

        await queries.GetTopSpendersAsync(10, CancellationToken.None);
        await queries.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(2, inner.TopSpenderCalls);
    }

    [Fact]
    public async Task A_broken_cache_falls_back_to_the_database()
    {
        var inner = new FakePlayerQueries();
        var queries = Create(inner, new ThrowingCache(), ttlSeconds: 60);

        var result = await queries.GetTopSpendersAsync(10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, inner.TopSpenderCalls);
    }
}
