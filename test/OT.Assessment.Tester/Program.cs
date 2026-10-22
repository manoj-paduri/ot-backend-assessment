using var settings = JsonDocument.Parse(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));

if (!settings.RootElement.TryGetProperty("Api", out var apiSettings)
    || apiSettings.ValueKind != JsonValueKind.Object
    || !apiSettings.TryGetProperty("EndpointUri", out var endpointSetting)
    || endpointSetting.ValueKind != JsonValueKind.String
    || !Uri.TryCreate(endpointSetting.GetString(), UriKind.Absolute, out var endpointUri)
    || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps)
    || !string.IsNullOrEmpty(endpointUri.Fragment))
{
    throw new InvalidOperationException(
        "Set Api:EndpointUri in appsettings.json to the full HTTP or HTTPS endpoint URI without a fragment.");
}

Console.WriteLine($"Sending casino wagers to {endpointUri}");

using var httpClient = new HttpClient();
var bg = new BogusGenerator();
var total = bg.Generate();
var scenario = Scenario.Create("casino_wager_submission", async context =>
    {
        var body = JsonSerializer.Serialize(total[(int)context.InvocationNumber]);
        using var request =
            Http.CreateRequest("POST", endpointUri.AbsoluteUri)
                .WithHeader("Accept", "application/json")
                .WithBody(new StringContent(body, Encoding.UTF8, "application/json"));

        return await Http.Send(httpClient, request);
    })
    .WithoutWarmUp()
    .WithLoadSimulations(
        Simulation.IterationsForInject(rate: 500,
            interval: TimeSpan.FromSeconds(2),
            iterations: 7000)
    );

NBomberRunner
    .RegisterScenarios(scenario)
    .WithWorkerPlugins(new HttpMetricsPlugin(new[] { HttpVersion.Version1 }))
    .WithoutReports()
    .Run();