using System.Threading.Channels;
using OT.Assessment.Consumer;

namespace OT.Assessment.Tests;

public class ChannelBatchReaderTests
{
    [Fact]
    public async Task Returns_a_full_batch_without_waiting_for_the_time_window()
    {
        var channel = Channel.CreateUnbounded<int>();
        for (var i = 0; i < 10; i++)
        {
            channel.Writer.TryWrite(i);
        }

        var batch = await ChannelBatchReader.ReadBatchAsync(
            channel.Reader, 5, TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, batch);
    }

    [Fact]
    public async Task Returns_a_partial_batch_when_the_time_window_elapses()
    {
        var channel = Channel.CreateUnbounded<int>();
        channel.Writer.TryWrite(1);
        channel.Writer.TryWrite(2);

        var batch = await ChannelBatchReader.ReadBatchAsync(
            channel.Reader, 100, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Equal(2, batch.Count);
    }

    [Fact]
    public async Task Returns_an_empty_batch_when_the_channel_is_completed_and_drained()
    {
        var channel = Channel.CreateUnbounded<int>();
        channel.Writer.Complete();

        var batch = await ChannelBatchReader.ReadBatchAsync(
            channel.Reader, 10, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.Empty(batch);
    }

    [Fact]
    public async Task Propagates_cancellation_requested_by_the_caller()
    {
        var channel = Channel.CreateUnbounded<int>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ChannelBatchReader.ReadBatchAsync(channel.Reader, 10, TimeSpan.FromSeconds(30), cts.Token));
    }
}
