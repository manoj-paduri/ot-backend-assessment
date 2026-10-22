namespace OT.Assessment.Core.Operations;

/// <summary>Operational read side: how far the pipeline has got.</summary>
public interface IIngestionMonitor
{
    Task<IngestionStats> GetStatsAsync(CancellationToken cancellationToken);
}
