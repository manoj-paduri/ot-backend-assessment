using Microsoft.Extensions.Caching.Distributed;
using OT.Assessment.Consumer;
using OT.Assessment.Core.Common;
using OT.Assessment.Core.Players;
using OT.Assessment.Core.Wagers;

namespace OT.Assessment.Tests.Support;

internal sealed class FakeWagerStore : IWagerStore
{
    private readonly Func<IReadOnlyCollection<CasinoWagerMessage>, int> _behaviour;

    public FakeWagerStore(Func<IReadOnlyCollection<CasinoWagerMessage>, int>? behaviour = null)
    {
        _behaviour = behaviour ?? (wagers => wagers.Count);
    }

    public List<int> CallSizes { get; } = new();

    public Task<int> SaveBatchAsync(IReadOnlyCollection<CasinoWagerMessage> wagers, CancellationToken cancellationToken)
    {
        CallSizes.Add(wagers.Count);
        return Task.FromResult(_behaviour(wagers));
    }
}

internal sealed class FakeAcknowledger : IMessageAcknowledger
{
    public List<ulong> AcknowledgedUpTo { get; } = new();
    public List<ulong> Rejected { get; } = new();

    public ValueTask AcknowledgeUpToAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        AcknowledgedUpTo.Add(deliveryTag);
        return ValueTask.CompletedTask;
    }

    public ValueTask RejectAsync(ulong deliveryTag, CancellationToken cancellationToken)
    {
        Rejected.Add(deliveryTag);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeWagerPublisher : IWagerPublisher
{
    public List<CasinoWagerMessage> Published { get; } = new();

    public Task PublishAsync(CasinoWagerMessage message, CancellationToken cancellationToken)
    {
        Published.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakePlayerQueries : IPlayerQueries
{
    public int TopSpenderCalls { get; private set; }

    public Task<PagedResult<PlayerWagerDto>> GetPlayerWagersAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var data = new List<PlayerWagerDto>
        {
            new(Guid.NewGuid(), "Game A", "Provider A", 10m, DateTime.UtcNow)
        };
        return Task.FromResult(PagedResult<PlayerWagerDto>.Create(data, page, pageSize, 35));
    }

    public Task<IReadOnlyList<TopSpenderDto>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        TopSpenderCalls++;
        IReadOnlyList<TopSpenderDto> result = new List<TopSpenderDto>
        {
            new(Guid.NewGuid(), "big-spender", 150m),
            new(Guid.NewGuid(), "small-spender", 100m)
        };
        return Task.FromResult(result);
    }
}

/// <summary>A cache that is always broken, to prove that a cache outage never breaks the endpoint.</summary>
internal sealed class ThrowingCache : IDistributedCache
{
    public byte[]? Get(string key) => throw new InvalidOperationException("cache down");
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("cache down");
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("cache down");
    public void Refresh(string key) => throw new InvalidOperationException("cache down");
    public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
    public void Remove(string key) => throw new InvalidOperationException("cache down");
    public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
}
