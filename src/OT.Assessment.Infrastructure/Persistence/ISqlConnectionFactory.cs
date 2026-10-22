using Microsoft.Data.SqlClient;

namespace OT.Assessment.Infrastructure.Persistence;

/// <summary>Hands out open SQL Server connections. Connections are pooled, so callers open late and dispose early.</summary>
public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
