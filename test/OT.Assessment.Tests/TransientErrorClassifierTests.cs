using OT.Assessment.Infrastructure.Resilience;

namespace OT.Assessment.Tests;

public class TransientErrorClassifierTests
{
    [Fact]
    public void Timeouts_and_io_failures_are_transient()
    {
        Assert.True(TransientErrorClassifier.IsTransient(new TimeoutException()));
        Assert.True(TransientErrorClassifier.IsTransient(new IOException()));
    }

    [Fact]
    public void A_connection_pool_timeout_is_transient()
    {
        var exception = new InvalidOperationException(
            "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.");

        Assert.True(TransientErrorClassifier.IsTransient(exception));
    }

    [Fact]
    public void Data_and_programming_errors_are_not_transient()
    {
        Assert.False(TransientErrorClassifier.IsTransient(new ArgumentException("bad value")));
        Assert.False(TransientErrorClassifier.IsTransient(new InvalidOperationException("boom")));
    }

    [Fact]
    public void Wrapped_exceptions_are_inspected()
    {
        Assert.True(TransientErrorClassifier.IsTransient(new Exception("outer", new TimeoutException())));
    }
}
