using System.Net.Sockets;
using Microsoft.Data.SqlClient;

namespace OT.Assessment.Infrastructure.Resilience;

/// <summary>
/// Decides whether a failure is worth retrying. Transient failures (database down, deadlock, timeout, network)
/// are retried with backoff and never lose a message; anything else is a problem with the data itself,
/// which retrying cannot fix.
/// </summary>
public static class TransientErrorClassifier
{
    // -1 connection failure, -2 timeout, 53/258 network, 64/233 connection dropped, 1205 deadlock victim,
    // 1222 lock timeout, 4060 database unavailable, 40xxx/49xxx Azure transient errors, 10928/10929 resource limits.
    private static readonly HashSet<int> TransientSqlErrorNumbers =
    [
        -1, -2, 53, 64, 233, 258, 1205, 1222, 4060, 10053, 10054, 10060, 10928, 10929,
        40197, 40501, 40613, 49918, 49919, 49920
    ];

    public static bool IsTransient(Exception exception) => exception switch
    {
        SqlException sql => IsTransientSqlException(sql),
        TimeoutException => true,
        IOException => true,
        SocketException => true,
        InvalidOperationException invalid => IsConnectionPoolTimeout(invalid),
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsTransient),
        _ => exception.InnerException is not null && IsTransient(exception.InnerException)
    };

    private static bool IsTransientSqlException(SqlException exception)
    {
        if (TransientSqlErrorNumbers.Contains(exception.Number))
        {
            return true;
        }

        foreach (SqlError error in exception.Errors)
        {
            // Severity 11-16 are errors caused by the statement or its data (conversion, truncation,
            // constraint violations): retrying cannot help. Higher severities are resource/server problems.
            if (TransientSqlErrorNumbers.Contains(error.Number) || error.Class > 16)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConnectionPoolTimeout(InvalidOperationException exception) =>
        exception.Message.Contains("Timeout expired", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("connection pool", StringComparison.OrdinalIgnoreCase);
}
