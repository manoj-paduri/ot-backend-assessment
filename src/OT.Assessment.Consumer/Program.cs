using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OT.Assessment.Consumer;
using OT.Assessment.Infrastructure.DependencyInjection;

// Content root = the folder of the executable, so appsettings.json is found no matter where the process is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services
    .Configure<WagerConsumerOptions>(builder.Configuration.GetSection(WagerConsumerOptions.SectionName))
    .AddRabbitMqConnection(builder.Configuration)
    .AddSqlServer(builder.Configuration)
    .AddWagerStore()
    .AddSingleton<WagerBatchProcessor>()
    .AddHostedService<WagerConsumerWorker>();

await builder.Build().RunAsync();
