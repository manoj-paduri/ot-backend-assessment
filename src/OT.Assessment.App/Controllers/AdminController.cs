using Microsoft.AspNetCore.Mvc;
using OT.Assessment.Core.Operations;

namespace OT.Assessment.App.Controllers;

/// <summary>
/// Operational endpoints. They exist so a load test can be reconciled without opening SQL Server or the
/// RabbitMQ console: submitted wagers = stored wagers + queued wagers + dead-lettered wagers.
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IIngestionMonitor _monitor;

    public AdminController(IIngestionMonitor monitor)
    {
        _monitor = monitor;
    }

    /// <summary>How many wagers are stored, still queued, or dead-lettered right now.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<IngestionStats>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestionStats>> GetStats(CancellationToken cancellationToken)
    {
        var stats = await _monitor.GetStatsAsync(cancellationToken);
        return Ok(stats);
    }
}
