using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Contracts;
using OT.Assessment.App.Controllers;
using OT.Assessment.Core.Common;
using OT.Assessment.Core.Players;
using OT.Assessment.Tests.Support;

namespace OT.Assessment.Tests;

public class PlayerControllerTests
{
    [Fact]
    public async Task Posting_a_wager_publishes_it_and_returns_202()
    {
        var publisher = new FakeWagerPublisher();
        var controller = new PlayerController(publisher, new FakePlayerQueries());
        var request = new CasinoWagerRequest
        {
            WagerId = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            TransactionId = Guid.NewGuid(),
            Username = "player",
            Provider = "Provider",
            GameName = "Game",
            Amount = 25m,
            CreatedDateTime = DateTime.UtcNow
        };

        var result = await controller.PostCasinoWager(request, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<WagerAcceptedResponse>(accepted.Value);
        Assert.Equal(request.WagerId, body.WagerId);
        var published = Assert.Single(publisher.Published);
        Assert.Equal(request.WagerId, published.WagerId);
        Assert.Equal(25m, published.Amount);
    }

    [Fact]
    public async Task Player_wagers_are_returned_with_paging_metadata()
    {
        var controller = new PlayerController(new FakeWagerPublisher(), new FakePlayerQueries());

        var result = await controller.GetPlayerWagers(Guid.NewGuid(), 1, 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<PlayerWagerDto>>(ok.Value);
        Assert.Equal(35, page.Total);
        Assert.Equal(4, page.TotalPages);
    }

    [Fact]
    public async Task Top_spenders_are_returned()
    {
        var controller = new PlayerController(new FakeWagerPublisher(), new FakePlayerQueries());

        var result = await controller.GetTopSpenders(2, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var spenders = Assert.IsAssignableFrom<IReadOnlyList<TopSpenderDto>>(ok.Value);
        Assert.Equal(2, spenders.Count);
        Assert.Equal("big-spender", spenders[0].Username);
    }
}
