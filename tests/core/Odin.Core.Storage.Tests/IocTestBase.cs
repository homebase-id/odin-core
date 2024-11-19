using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests;

public abstract class IocTestBase
{
    protected string TempFolder;
    protected ILifetimeScope Services = null!;
    protected LogEventMemoryStore LogEventMemoryStore = null!;
    protected Guid IdentityId;

    [SetUp]
    public void Setup()
    {
        IdentityId = Guid.NewGuid();
        LogEventMemoryStore = new LogEventMemoryStore();
        TempFolder = TempDirectory.Create();
    }

    [TearDown]
    public void TearDown()
    {
        Services?.Dispose();
        Directory.Delete(TempFolder, true);
        LogEvents.AssertEvents(LogEventMemoryStore.GetLogEvents());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    protected async Task RegisterServicesAsync(DatabaseType databaseType)
    {
        var builder = new ContainerBuilder();

        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(LogEventMemoryStore))
            .SingleInstance();
        builder
            .RegisterInstance(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(LogEventMemoryStore))
            .SingleInstance();

        builder.AddDatabaseCacheServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(TempFolder, "system-test.db"));
                builder.AddSqliteIdentityDatabaseServices(IdentityId, Path.Combine(TempFolder, "identity-test.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices("Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin");
                builder.AddPgsqlIdentityDatabaseServices(IdentityId, "Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin");
                break;
            default:
                throw new Exception("Unsupported database type");
        }

        Services = builder.Build();

        var systemDatabase = Services.Resolve<SystemDatabase>();
        await systemDatabase.CreateDatabaseAsync(true);

        var identityDatabase = Services.Resolve<IdentityDatabase>();
        await identityDatabase.CreateDatabaseAsync(true);
    }
}