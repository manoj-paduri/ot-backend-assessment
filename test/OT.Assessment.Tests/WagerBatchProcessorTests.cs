using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OT.Assessment.Consumer;
using OT.Assessment.Tests.Support;

namespace OT.Assessment.Tests;

public class WagerBatchProcessorTests
{
    private static WagerBatchProcessor CreateProcessor(FakeWagerStore store) =>
        new(
            store,
            Options.Create(new WagerConsumerOptions { RetryInitialDelayMilliseconds = 1, RetryMaxDelayMilliseconds = 5 }),
            NullLogger<WagerBatchProcessor>.Instance);

    [Fact]
    public async Task A_valid_batch_is_stored_once_and_acknowledged_up_to_the_highest_tag()
    {
        var store = new FakeWagerStore();
        var acknowledger = new FakeAcknowledger();
        var deliveries = new[]
        {
            TestData.Delivery(1, TestData.Wager()),
            TestData.Delivery(2, TestData.Wager()),
            TestData.Delivery(3, TestData.Wager())
        };

        await CreateProcessor(store).ProcessAsync(deliveries, acknowledger, CancellationToken.None);

        Assert.Equal(new[] { 3 }, store.CallSizes);
        Assert.Equal(new ulong[] { 3 }, acknowledger.AcknowledgedUpTo);
        Assert.Empty(acknowledger.Rejected);
    }

    [Fact]
    public async Task A_malformed_message_is_dead_lettered_without_blocking_the_others()
    {
        var store = new FakeWagerStore();
        var acknowledger = new FakeAcknowledger();
        var deliveries = new[]
        {
            TestData.Delivery(1, TestData.Wager()),
            new WagerDelivery(2, new byte[] { 1, 2, 3 }),
            TestData.Delivery(3, TestData.Wager())
        };

        await CreateProcessor(store).ProcessAsync(deliveries, acknowledger, CancellationToken.None);

        Assert.Equal(new[] { 2 }, store.CallSizes);
        Assert.Equal(new ulong[] { 2 }, acknowledger.Rejected);
        Assert.Equal(new ulong[] { 3 }, acknowledger.AcknowledgedUpTo);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_nothing_is_dead_lettered()
    {
        var attempts = 0;
        var store = new FakeWagerStore(wagers =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("database busy");
            }

            return wagers.Count;
        });
        var acknowledger = new FakeAcknowledger();

        await CreateProcessor(store).ProcessAsync(
            new[] { TestData.Delivery(1, TestData.Wager()), TestData.Delivery(2, TestData.Wager()) },
            acknowledger,
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Empty(acknowledger.Rejected);
        Assert.Equal(new ulong[] { 2 }, acknowledger.AcknowledgedUpTo);
    }

    [Fact]
    public async Task A_poison_wager_is_isolated_and_the_rest_of_the_batch_is_still_stored()
    {
        var store = new FakeWagerStore(wagers =>
        {
            if (wagers.Any(w => w.Username == "poison"))
            {
                throw new ArgumentException("cannot be stored");
            }

            return wagers.Count;
        });
        var acknowledger = new FakeAcknowledger();
        var deliveries = new[]
        {
            TestData.Delivery(1, TestData.Wager("ok-1")),
            TestData.Delivery(2, TestData.Wager("poison")),
            TestData.Delivery(3, TestData.Wager("ok-2"))
        };

        await CreateProcessor(store).ProcessAsync(deliveries, acknowledger, CancellationToken.None);

        Assert.Equal(new[] { 3, 1, 1, 1 }, store.CallSizes); // the whole batch, then one by one
        Assert.Equal(new ulong[] { 2 }, acknowledger.Rejected);
        Assert.Equal(new ulong[] { 3 }, acknowledger.AcknowledgedUpTo);
    }
}
