using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OT.Assessment.Core.Operations;
using OT.Assessment.Core.Players;
using OT.Assessment.Core.Wagers;
using OT.Assessment.Infrastructure.Caching;
using OT.Assessment.Infrastructure.Messaging;
using OT.Assessment.Infrastructure.Operations;
using OT.Assessment.Infrastructure.Persistence;
using StackExchange.Redis;

namespace OT.Assessment.Infrastructure.DependencyInjection;

/// <summary>Composition helpers so the API and the consumer wire the shared infrastructure identically.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the RabbitMQ options and the shared connection.</summary>
    public static IServiceCollection AddRabbitMqConnection(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<RabbitMqConnectionProvider>();
        return services;
    }

    /// <summary>Registers the publisher used by the API (requires <see cref="AddRabbitMqConnection"/>).</summary>
    public static IServiceCollection AddWagerPublisher(this IServiceCollection services)
    {
        services.AddSingleton<RabbitMqWagerPublisher>();
        services.AddSingleton<IWagerPublisher>(sp => sp.GetRequiredService<RabbitMqWagerPublisher>());
        return services;
    }

    /// <summary>Registers the SQL Server connection factory and database options.</summary>
    public static IServiceCollection AddSqlServer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{DatabaseOptions.ConnectionStringName}' is not configured.");
        }

        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));
        return services;
    }

    /// <summary>Registers the write side used by the consumer (requires <see cref="AddSqlServer"/>).</summary>
    public static IServiceCollection AddWagerStore(this IServiceCollection services)
    {
        services.AddSingleton<IWagerStore, SqlWagerStore>();
        return services;
    }

    /// <summary>
    /// Registers the monitor behind <c>GET api/admin/stats</c>
    /// (requires <see cref="AddRabbitMqConnection"/> and <see cref="AddSqlServer"/>).
    /// </summary>
    public static IServiceCollection AddIngestionMonitor(this IServiceCollection services)
    {
        services.AddSingleton<IIngestionMonitor, IngestionMonitor>();
        return services;
    }

    /// <summary>
    /// Registers the read side used by the API: SQL Server queries wrapped in a distributed cache.
    /// Redis is used when <c>Redis:ConnectionString</c> is set, otherwise an in-memory cache keeps the app self-contained.
    /// </summary>
    public static IServiceCollection AddPlayerQueries(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CachingOptions>(configuration.GetSection(CachingOptions.SectionName));

        var redisConnectionString = configuration["Redis:ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                var redis = ConfigurationOptions.Parse(redisConnectionString);
                redis.AbortOnConnectFail = false; // start even if Redis is not up yet
                redis.ConnectTimeout = 2000;
                redis.AsyncTimeout = 1000;
                options.ConfigurationOptions = redis;
                options.InstanceName = configuration["Redis:InstanceName"] ?? "ot-assessment:";
            });
        }

        services.AddSingleton<SqlPlayerQueries>();
        services.AddSingleton<IPlayerQueries>(sp => new CachedPlayerQueries(
            sp.GetRequiredService<SqlPlayerQueries>(),
            sp.GetRequiredService<IDistributedCache>(),
            sp.GetRequiredService<IOptions<CachingOptions>>(),
            sp.GetRequiredService<ILogger<CachedPlayerQueries>>()));

        return services;
    }
}
