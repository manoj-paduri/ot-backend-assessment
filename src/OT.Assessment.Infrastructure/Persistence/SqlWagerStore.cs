using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Wagers;

namespace OT.Assessment.Infrastructure.Persistence;

/// <summary>
/// Writes wagers with one round trip per batch: the batch is streamed to <c>dbo.usp_Wager_InsertBatch</c>
/// as a table-valued parameter, and the procedure resolves providers/games/players, inserts the new wagers
/// and maintains the per-player totals inside a single transaction.
/// </summary>
public sealed class SqlWagerStore : IWagerStore
{
    private const string InsertBatchProcedure = "dbo.usp_Wager_InsertBatch";
    private const string BatchTypeName = "dbo.WagerBatchType";

    // Column order and types must mirror dbo.WagerBatchType in DatabaseGenerate.sql.
    private static readonly SqlMetaData[] BatchColumns =
    [
        new SqlMetaData("WagerId", SqlDbType.UniqueIdentifier),
        new SqlMetaData("AccountId", SqlDbType.UniqueIdentifier),
        new SqlMetaData("Username", SqlDbType.NVarChar, 100),
        new SqlMetaData("Provider", SqlDbType.NVarChar, 200),
        new SqlMetaData("GameName", SqlDbType.NVarChar, 200),
        new SqlMetaData("Theme", SqlDbType.NVarChar, 100),
        new SqlMetaData("TransactionId", SqlDbType.UniqueIdentifier),
        new SqlMetaData("CountryCode", SqlDbType.VarChar, 3),
        new SqlMetaData("Amount", SqlDbType.Decimal, (byte)19, (byte)4),
        new SqlMetaData("NumberOfBets", SqlDbType.Int),
        new SqlMetaData("Duration", SqlDbType.BigInt),
        new SqlMetaData("CreatedDateTimeUtc", SqlDbType.DateTime2)
    ];

    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly int _commandTimeoutSeconds;

    public SqlWagerStore(ISqlConnectionFactory connectionFactory, IOptions<DatabaseOptions> options)
    {
        _connectionFactory = connectionFactory;
        _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
    }

    public async Task<int> SaveBatchAsync(IReadOnlyCollection<CasinoWagerMessage> wagers, CancellationToken cancellationToken)
    {
        if (wagers.Count == 0)
        {
            return 0;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = InsertBatchProcedure;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = _commandTimeoutSeconds;

        var parameter = command.Parameters.Add("@Wagers", SqlDbType.Structured);
        parameter.TypeName = BatchTypeName;
        parameter.Value = ToRecords(wagers);

        var inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(inserted);
    }

    /// <summary>Streams the batch as <see cref="SqlDataRecord"/>s (the record instance is reused, as documented for TVPs).</summary>
    private static IEnumerable<SqlDataRecord> ToRecords(IEnumerable<CasinoWagerMessage> wagers)
    {
        var record = new SqlDataRecord(BatchColumns);

        foreach (var wager in wagers)
        {
            record.SetGuid(0, wager.WagerId);
            record.SetGuid(1, wager.AccountId);
            record.SetString(2, wager.Username);
            record.SetString(3, wager.Provider);
            record.SetString(4, wager.GameName);
            SetNullableString(record, 5, wager.Theme);
            record.SetGuid(6, wager.TransactionId);
            SetNullableString(record, 7, wager.CountryCode);
            record.SetDecimal(8, decimal.Round(wager.Amount, 4, MidpointRounding.AwayFromZero));
            record.SetInt32(9, wager.NumberOfBets);
            record.SetInt64(10, wager.Duration);
            record.SetDateTime(11, wager.CreatedDateTimeUtc);

            yield return record;
        }
    }

    private static void SetNullableString(SqlDataRecord record, int ordinal, string? value)
    {
        if (value is null)
        {
            record.SetDBNull(ordinal);
        }
        else
        {
            record.SetString(ordinal, value);
        }
    }
}
