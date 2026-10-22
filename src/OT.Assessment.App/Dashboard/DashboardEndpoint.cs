namespace OT.Assessment.App.Dashboard;

/// <summary>
/// Serves the live ingestion dashboard at <c>/</c>. The page is embedded in the assembly, so it works the same
/// with <c>dotnet run</c>, from a published folder or inside a container, with no static-file configuration.
/// </summary>
public static class DashboardEndpoint
{
    private const string ResourceName = "dashboard.html";

    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        var html = ReadEmbeddedPage();
        endpoints.MapGet("/", () => Results.Content(html, "text/html; charset=utf-8")).ExcludeFromDescription();
        return endpoints;
    }

    private static string ReadEmbeddedPage()
    {
        using var stream = typeof(DashboardEndpoint).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
