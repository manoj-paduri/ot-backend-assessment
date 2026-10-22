using OT.Assessment.Core.Common;

namespace OT.Assessment.Core.Players;

/// <summary>Read side of the system: the queries exposed by the API.</summary>
public interface IPlayerQueries
{
    /// <summary>Latest wagers of one player, newest first. <paramref name="page"/> is 1-based.</summary>
    Task<PagedResult<PlayerWagerDto>> GetPlayerWagersAsync(
        Guid accountId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The players that spent the most, highest first.</summary>
    Task<IReadOnlyList<TopSpenderDto>> GetTopSpendersAsync(int count, CancellationToken cancellationToken);
}
