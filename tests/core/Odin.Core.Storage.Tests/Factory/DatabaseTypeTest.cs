using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Storage.Tests.Factory;

public class DatabaseTypeTest
{
    private string _tempFolder;
    private ILifetimeScope _services = null!;
    private LogEventMemoryStore _logEventMemoryStore = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = TempDirectory.Create();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        Directory.Delete(_tempFolder, true);
        LogEvents.AssertEvents(_logEventMemoryStore.GetLogEvents());
    }

    private void RegisterServices(DatabaseType databaseType)
    {
        _logEventMemoryStore = new LogEventMemoryStore();

        var services = new ServiceCollection();
        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedSystemConnectionFactory>(_logEventMemoryStore));
        services.AddSingleton(TestLogFactory.CreateConsoleLogger<ScopedIdentityConnectionFactory>(_logEventMemoryStore));
        services.AddScoped<ScopedConnectionFactoryTest.ScopedSystemUser>();
        services.AddTransient<ScopedConnectionFactoryTest.TransientSystemUser>();

        var identityId = Guid.NewGuid();

        var builder = new ContainerBuilder();
        builder.Populate(services);

        builder.AddDatabaseCacheServices();
        builder.AddDatabaseCounterServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(_tempFolder, "system-test.db"));
                builder.AddSqliteIdentityDatabaseServices(identityId, Path.Combine(_tempFolder, "identity-test.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices("Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin");
                builder.AddPgsqlIdentityDatabaseServices(identityId, "Host=localhost;Port=5432;Database=odin;Username=odin;Password=odin");
                break;
            default:
                throw new Exception("Unsupported database type");
        }

        _services = builder.Build();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldReturnCorrectDatabaseType(DatabaseType databaseType)
    {
        RegisterServices(databaseType);

        await using var scope = _services.BeginLifetimeScope();

        var scopedSystemConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();
        Assert.That(scopedSystemConnectionFactory.DatabaseType, Is.EqualTo(databaseType));

        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        Assert.That(scopedIdentityConnectionFactory.DatabaseType, Is.EqualTo(databaseType));
    }


}