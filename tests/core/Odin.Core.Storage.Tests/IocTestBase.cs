using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Cache;
using Odin.Core.Identity;
using Odin.Core.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;
using Serilog.Events;
using Testcontainers.PostgreSql;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

namespace Odin.Core.Storage.Tests;

public abstract class IocTestBase
{
    protected static DatabaseType DatabaseType;
    protected string TempFolder;
    protected ILifetimeScope Services = null!;
    protected LogEventMemoryStore LogEventMemoryStore = null!;
    protected Guid IdentityId;
    protected PostgreSqlContainer PostgresContainer;

    [SetUp]
    public virtual void Setup()
    {
        IdentityId = Guid.NewGuid();
        LogEventMemoryStore = new LogEventMemoryStore();
        TempFolder = TempDirectory.Create();
    }

    [TearDown]
    public virtual void TearDown()
    {
        Services?.Dispose();

        PostgresContainer?.DisposeAsync().AsTask().Wait();
        PostgresContainer = null;

        Directory.Delete(TempFolder, true);
        LogEvents.DumpErrorEvents(LogEventMemoryStore.GetLogEvents());
        LogEvents.AssertEvents(LogEventMemoryStore.GetLogEvents());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    protected virtual async Task RegisterServicesAsync(
        DatabaseType databaseType,
        bool createDatabases = true,
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
        serviceCollection.AddCoreCacheServices(new CacheConfiguration
        {
            Level2CacheType = Level2CacheType.None,
        });

        var builder = new ContainerBuilder();
        builder.Populate(serviceCollection);

        builder.RegisterInstance(new OdinIdentity(IdentityId, "foo.bar")).SingleInstance();

        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<DbConnectionPool>(LogEventMemoryStore, logEventLevel))
            .SingleInstance();
        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(LogEventMemoryStore, logEventLevel))
            .SingleInstance();
        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(LogEventMemoryStore, logEventLevel))
            .SingleInstance();

        builder.RegisterModule(new LoggingAutofacModule());
        builder.RegisterGeneric(typeof(GenericMemoryCache<>)).As(typeof(IGenericMemoryCache<>)).SingleInstance();

        builder.AddDatabaseCacheServices();
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
            await systemDatabase.CreateDatabaseAsync(true);

            var identityDatabase = Services.Resolve<IdentityDatabase>();
            await identityDatabase.CreateDatabaseAsync(true);
        }
    }
}