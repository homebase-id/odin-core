using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database;

public class MigratorTests : IocTestBase
{
    private async Task<bool> TableExistsAsync(string tableName)
    {
        var scopedConnectionFactory = Services.Resolve<ScopedSystemConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        return await cn.TableExistsAsync(tableName);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldCreateAndUpdateTableVersionInfoWithEmptyMigrationList(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, false);
        await using var scope = Services.BeginLifetimeScope();
        var logger = scope.Resolve<ILogger<SystemMigrator>>();
        var scopedConnectionFactory = scope.Resolve<ScopedSystemConnectionFactory>();

        // SEB:NOTE we use a mock, so we can create a new, empty SortedMigrations list
        var mock = new Mock<SystemMigrator>(logger, scopedConnectionFactory)
        {
            CallBase = true
        };
        mock.Setup(x => x.SortedMigrations).Returns([]);

        var migrator  = mock.Object;

        //
        // Make sure the VersionInfo table DOES NOT exist
        //
        {
            var tableExists = await TableExistsAsync("VersionInfo");
            Assert.That(tableExists, Is.False);
        }

        await migrator.MigrateAsync();

        //
        // Make sure the VersionInfo table exists
        //
        {
            var tableExists = await TableExistsAsync("VersionInfo");
            Assert.That(tableExists, Is.True);
        }

        //
        // Make sure the VersionInfo is empty (version is -1)
        //
        {
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(-1));
        }

        //
        // Make sure we can insert the version and read it back
        //
        {
            var testVersion1 = 20250729094412L;

            await migrator.SetCurrentVersionAsync(testVersion1);

            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(testVersion1));
        }

        //
        // Make sure we can update the version and read it back
        //
        {
            var testVersion2 = 20250729094413L;

            await migrator.SetCurrentVersionAsync(testVersion2);

            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(testVersion2));
        }
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldMigrateTheDatabaseWithRealMigrations(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<SystemMigrator>();

        // Make sure tables do not exist before migration
        foreach (var tableType in SystemDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync(table.TableName);
            Assert.That(tableExists, Is.False);
        }

        await migrator.MigrateAsync();

        // Make sure tables exist after migration
        foreach (var tableType in SystemDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync(table.TableName);
            Assert.That(tableExists, Is.True);
        }
    }

    //

}


