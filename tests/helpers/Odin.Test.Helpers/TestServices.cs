using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Logging;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;
using Odin.Services.Registry.LastSeen;
using Odin.Test.Helpers.Logging;
using Serilog.Events;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Odin.Test.Helpers;

public class TestServices : IDisposable
{
    private PostgreSqlContainer? _postgresContainer;
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    //

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _services?.Dispose();
        _postgresContainer?.StopAsync().Wait();
        _postgresContainer?.DisposeAsync().AsTask().Wait();
        _redisContainer?.StopAsync().Wait();
        _redisContainer?.DisposeAsync().AsTask().Wait();
    }

    //

    public async Task<ILifetimeScope> RegisterServicesAsync(
        DatabaseType databaseType,
        string tempFolder,
        bool migrateDatabases = true,
        bool redisEnabled = false,
        LogEventLevel logEventLevel = LogEventLevel.Debug)
    {
        if (!Directory.Exists(tempFolder))
        {
            throw new DirectoryNotFoundException(tempFolder);
        }

        var dummyIdentityId = Guid.NewGuid();

        if (databaseType == DatabaseType.Postgres)
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            await _postgresContainer.StartAsync();
        }

        var serviceCollection = new ServiceCollection();

        var level2CacheType = Level2CacheType.None;
        if (redisEnabled)
        {
            _redisContainer = new RedisBuilder().WithImage("redis:latest").Build();
            await _redisContainer.StartAsync();

            var redisConfig = _redisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
            serviceCollection.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfig));

            level2CacheType = Level2CacheType.Redis;
        }

        serviceCollection.AddCoreCacheServices(new CacheConfiguration
        {
            Level2CacheType = level2CacheType
        });

        if (redisEnabled)
        {
            serviceCollection.AddSingleton<INodeLock, RedisLock>();
        }
        else
        {
            serviceCollection.AddSingleton<INodeLock, NodeLock>();
        }

        var builder = new ContainerBuilder();
        builder.Populate(serviceCollection);

        builder.RegisterInstance(new OdinIdentity(dummyIdentityId, "foo.bar")).SingleInstance();

        builder.RegisterModule(new LoggingAutofacModule());
        builder.RegisterGeneric(typeof(TestConsoleLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        builder.RegisterGeneric(typeof(GenericMemoryCache<>)).As(typeof(IGenericMemoryCache<>)).SingleInstance();
        builder.RegisterType<LastSeenService>().As<ILastSeenService>().InstancePerLifetimeScope();

        builder.AddDatabaseCounterServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(tempFolder, "system-test.db"));
                builder.AddSqliteIdentityDatabaseServices(dummyIdentityId, Path.Combine(tempFolder, "identity-test.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices(_postgresContainer!.GetConnectionString());
                builder.AddPgsqlIdentityDatabaseServices(dummyIdentityId, _postgresContainer!.GetConnectionString());
                break;
            default:
                throw new Exception("Unsupported database type");
        }

        builder.AddSystemCaches();
        builder.AddTenantCaches(dummyIdentityId.ToString());

        _services = builder.Build();

        if (migrateDatabases)
        {
            var systemDatabase = _services.Resolve<SystemDatabase>();
            await systemDatabase.MigrateDatabaseAsync();

            var identityDatabase = _services.Resolve<IdentityDatabase>();
            await identityDatabase.MigrateDatabaseAsync();
        }

        return _services;
    }

    //

}