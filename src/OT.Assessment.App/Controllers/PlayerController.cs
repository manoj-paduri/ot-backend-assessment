using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Contracts;
using OT.Assessment.Core.Common;
using OT.Assessment.Core.Players;
using OT.Assessment.Core.Wagers;

namespace OT.Assessment.App.Controllers;

[ApiController]
[Route("api/player")]
public sealed class PlayerController : ControllerBase
{
    private readonly IWagerPublisher _publisher;
    private readonly IPlayerQueries _queries;

    public PlayerController(IWagerPublisher publisher, IPlayerQueries queries)
    {
        _publisher = publisher;
        _queries = queries;
    }

    /// <summary>Accepts a casino wager and publishes it to the message bus for asynchronous storage.</summary>
    /// <response code="202">The broker has accepted the wager; it will be stored by the consumer.</response>
    /// <response code="400">The payload is invalid.</response>
    /// <response code="503">The message bus is unavailable; the client may retry.</response>
    [HttpPost("casinowager")]
    [ProducesResponseType<WagerAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PostCasinoWager(
        [FromBody] CasinoWagerRequest request, CancellationToken cancellationToken)
    {
        var message = request.ToMessage();
        await _publisher.PublishAsync(message, cancellationToken);
        return Accepted(new WagerAcceptedResponse(message.WagerId));
    }

    /// <summary>Latest casino wagers of a player, newest first, paginated. <paramref name="playerId"/> is the wager's accountId.</summary>
    [HttpGet("{playerId:guid}/casino")]
    [ProducesResponseType<PagedResult<PlayerWagerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PlayerWagerDto>>> GetPlayerWagers(
        Guid playerId,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.GetPlayerWagersAsync(playerId, page, pageSize, cancellationToken);
        return Ok(result);
    }

    /// <summary>The players that spent the most, highest first.</summary>
    [HttpGet("topSpenders")]
    [ProducesResponseType<IReadOnlyList<TopSpenderDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<TopSpenderDto>>> GetTopSpenders(
        [FromQuery, Range(1, 1000)] int count = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.GetTopSpendersAsync(count, cancellationToken);
        return Ok(result);
    }
}
