using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Common;
using OT.Assessment.Core.Players;

namespace OT.Assessment.Infrastructure.Persistence;

/// <summary>Read side, implemented with Dapper on top of stored procedures.</summary>
public sealed class SqlPlayerQueries : IPlayerQueries
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly int _commandTimeoutSeconds;

    public SqlPlayerQueries(ISqlConnectionFactory connectionFactory, IOptions<DatabaseOptions> options)
    {
        _connectionFactory = connectionFactory;
        _commandTimeoutSeconds = options.Value.CommandTimeoutSeconds;
    }

    public async Task<PagedResult<PlayerWagerDto>> GetPlayerWagersAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            "dbo.usp_Wager_GetPageByAccount",
            new { AccountId = accountId, Page = page, PageSize = pageSize },
            commandType: CommandType.StoredProcedure,
            commandTimeout: _commandTimeoutSeconds,
            cancellationToken: cancellationToken);

        // The procedure returns two result sets: the requested page, then the player's total wager count.
        using var results = await connection.QueryMultipleAsync(command).ConfigureAwait(false);
        var rows = (await results.ReadAsync<WagerRow>().ConfigureAwait(false)).ToList();
        var total = await results.ReadSingleAsync<int>().ConfigureAwait(false);

        var data = rows
            .Select(row => new PlayerWagerDto(
                row.WagerId,
                row.Game,
                row.Provider,
                row.Amount,
                DateTime.SpecifyKind(row.CreatedDate, DateTimeKind.Utc)))
            .ToList();

        return PagedResult<PlayerWagerDto>.Create(data, page, pageSize, total);
    }

    public async Task<IReadOnlyList<TopSpenderDto>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var command = new CommandDefinition(
            "dbo.usp_Player_GetTopSpenders",
            new { Count = count },
            commandType: CommandType.StoredProcedure,
            commandTimeout: _commandTimeoutSeconds,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<TopSpenderRow>(command).ConfigureAwait(false);
        return rows
            .Select(row => new TopSpenderDto(row.AccountId, row.Username, row.TotalAmountSpend))
            .ToList();
    }

    // Row shapes for Dapper (the column names come from the stored procedures).
    internal sealed class WagerRow
    {
        public Guid WagerId { get; set; }
        public string Game { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    internal sealed class TopSpenderRow
    {
        public Guid AccountId { get; set; }
        public string Username { get; set; } = string.Empty;
        public decimal TotalAmountSpend { get; set; }
    }
}
