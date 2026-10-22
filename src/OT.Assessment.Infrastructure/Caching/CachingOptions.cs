namespace OT.Assessment.Infrastructure.Caching;

/// <summary>Caching settings (section <c>Caching</c>).</summary>
public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    /// <summary>
    /// How long the top-spenders list may be served from cache. The list is an aggregate over the whole
    /// player base, so a few seconds of staleness is invisible to users but removes almost all database load.
    /// </summary>
    public int TopSpendersTtlSeconds { get; set; } = 5;
}
