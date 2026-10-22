using System.Threading.Channels;

namespace OT.Assessment.Consumer;

/// <summary>
/// Turns a stream of items into batches: a batch is returned when it reaches <c>maxSize</c> or when
/// <c>maxWait</c> has passed since its first item, whichever comes first. This is what lets the consumer
/// write large batches under load without holding on to a lone message when traffic is quiet.
/// </summary>
public static class ChannelBatchReader
{
    /// <summary>
    /// Waits for at least one item, then collects more until the batch is full or the wait elapses.
    /// Returns an empty list only when the channel has been completed and drained.
    /// </summary>
    public static async Task<List<T>> ReadBatchAsync<T>(
        ChannelReader<T> reader, int maxSize, TimeSpan maxWait, CancellationToken cancellationToken)
    {
        var batch = new List<T>(maxSize);

        if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return batch;
        }

        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        windowCts.CancelAfter(maxWait);

        try
        {
            while (batch.Count < maxSize)
            {
                if (reader.TryRead(out var item))
                {
                    batch.Add(item);
                    continue;
                }

                if (!await reader.WaitToReadAsync(windowCts.Token).ConfigureAwait(false))
                {
                    break; // channel completed
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the batching window elapsed: hand over what has been collected.
        }

        return batch;
    }
}
