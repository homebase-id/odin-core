using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;
using Serilog.Events;
using Testcontainers.PostgreSql;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Odin.Core.Storage.Tests;

public abstract class IocTestBase
{
    protected static DatabaseType DatabaseType;
    protected string TempFolder;
    protected ILifetimeScope Services;
    protected Guid IdentityId;
    protected PostgreSqlContainer PostgresContainer;
    protected RedisContainer RedisContainer;

    [SetUp]
    public virtual void Setup()
    {
        IdentityId = Guid.NewGuid();
        TempFolder = TempDirectory.Create();
    }

    [TearDown]
    public virtual void TearDown()
    {
        var logEventMemoryStore = Services?.Resolve<ILogEventMemoryStore>();
        if (logEventMemoryStore != null)
        {
            LogEvents.DumpErrorEvents(logEventMemoryStore.GetLogEvents());
            LogEvents.AssertEvents(logEventMemoryStore.GetLogEvents());
        }

        Services?.Dispose();
        Services = null;

        PostgresContainer?.DisposeAsync().AsTask().Wait();
        PostgresContainer = null;

        RedisContainer?.StopAsync().Wait();
        RedisContainer?.DisposeAsync().AsTask().Wait();;
        RedisContainer = null;

        Directory.Delete(TempFolder, true);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    protected virtual async Task RegisterServicesAsync(
        DatabaseType databaseType,
        bool createDatabases = true,
        bool redisEnabled = false,
        LogEventLevel logEventLevel = LogEventLevel.Debug)
    {
        DatabaseType = databaseType;

        if (databaseType == DatabaseType.Postgres)
        {
            PostgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            await PostgresContainer.StartAsync();
        }

        var serviceCollection = new ServiceCollection();

        var level2CacheType = Level2CacheType.None;
        if (redisEnabled)
        {
            RedisContainer = new RedisBuilder().WithImage("redis:latest").Build();
            await RedisContainer.StartAsync();

            var redisConfig = RedisContainer?.GetConnectionString() ?? throw new InvalidOperationException();
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

        builder.RegisterInstance(new OdinIdentity(IdentityId, "foo.bar")).SingleInstance();

        builder.RegisterModule(new LoggingAutofacModule());
        builder.RegisterGeneric(typeof(TestConsoleLogger<>)).As(typeof(ILogger<>)).SingleInstance();

        builder.RegisterGeneric(typeof(GenericMemoryCache<>)).As(typeof(IGenericMemoryCache<>)).SingleInstance();

        builder.AddDatabaseCounterServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(TempFolder, "system-test.db"));
                builder.AddSqliteIdentityDatabaseServices(IdentityId, Path.Combine(TempFolder, "identity-test.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices(PostgresContainer.GetConnectionString());
                builder.AddPgsqlIdentityDatabaseServices(IdentityId, PostgresContainer.GetConnectionString());
                break;
            default:
                throw new Exception("Unsupported database type");
        }

        builder.AddSystemCaches();
        builder.AddTenantCaches(IdentityId.ToString());

        Services = builder.Build();

        if (createDatabases)
        {
            var systemDatabase = Services.Resolve<SystemDatabase>();
            await systemDatabase.MigrateDatabaseAsync();

            var identityDatabase = Services.Resolve<IdentityDatabase>();
            await identityDatabase.MigrateDatabaseAsync();
        }
    }
}