using Microsoft.AspNetCore.Mvc;
using OT.Assessment.App.Controllers;
using OT.Assessment.Core.Operations;

namespace OT.Assessment.Tests;

public class AdminControllerTests
{
    [Fact]
    public async Task Stats_are_returned_as_reported_by_the_monitor()
    {
        var expected = new IngestionStats(7000, 950, 0, 0, DateTime.UtcNow);
        var controller = new AdminController(new FakeIngestionMonitor(expected));

        var result = await controller.GetStats(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(expected, ok.Value);
    }

    private sealed class FakeIngestionMonitor : IIngestionMonitor
    {
        private readonly IngestionStats _stats;

        public FakeIngestionMonitor(IngestionStats stats)
        {
            _stats = stats;
        }

        public Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken) => Task.FromResult(_stats);
    }
}
