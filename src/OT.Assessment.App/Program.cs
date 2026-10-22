using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OT.Assessment.App.Dashboard;
using OT.Assessment.App.ErrorHandling;
using OT.Assessment.App.HealthChecks;
using OT.Assessment.App.Middleware;
using OT.Assessment.Infrastructure.DependencyInjection;

// Content root = the folder of the executable, so appsettings.json is found no matter where the process is started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services
    .AddRabbitMqConnection(builder.Configuration)
    .AddWagerPublisher()
    .AddSqlServer(builder.Configuration)
    .AddPlayerQueries(builder.Configuration)
    .AddIngestionMonitor();

builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>("sqlserver", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

// Pipeline order: correlation id first so everything below (including error handling) logs with it.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();

app.MapOpenApi();
app.MapControllers();
app.MapDashboard();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
