using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database;

public class AbstractMigratorTests : IocTestBase
{
    private async Task<bool> TableExistsAsync<T>(string tableName) where T : IScopedConnectionFactory
    {
        var scopedConnectionFactory = Services.Resolve<T>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        return await cn.TableExistsAsync(tableName);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite, false)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
    #endif
    public async Task ItShouldCreateAndUpdateTableVersionInfoWithEmptyMigrationList(DatabaseType databaseType, bool redisEnabled)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();
        var logger = scope.Resolve<ILogger<IdentityMigrator>>();
        var scopedConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        var nodeLock = scope.Resolve<INodeLock>();

        // SEB:NOTE we use a mock, so we can create a new, empty SortedMigrations list
        var mock = new Mock<IdentityMigrator>(logger, scopedConnectionFactory, nodeLock)
        {
            CallBase = true
        };
        mock.Setup(x => x.SortedMigrations).Returns([]);

        var migrator  = mock.Object;

        //
        // Make sure the VersionInfo table DOES NOT exist
        //
        {
            var tableExists = await TableExistsAsync<ScopedIdentityConnectionFactory>("VersionInfo");
            Assert.That(tableExists, Is.False);
        }

        await migrator.MigrateAsync();

        //
        // Make sure the VersionInfo table exists
        //
        {
            var tableExists = await TableExistsAsync<ScopedIdentityConnectionFactory>("VersionInfo");
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
    [TestCase(DatabaseType.Sqlite, false)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres, true)]
#endif
    public async Task ItShouldMigrateTheDatabaseWithRealMigrations(DatabaseType databaseType, bool redisEnabled)
    {
        // NOTE: we migrate both system and identity here.
        // This is to check that there isn't a conflict between the two on postgres, since all
        // tables are in the same database here.

        await RegisterServicesAsync(databaseType, createDatabases: false, redisEnabled: redisEnabled);
        await using var scope = Services.BeginLifetimeScope();

        var systemMigrator = scope.Resolve<SystemMigrator>();
        var identityMigrator = scope.Resolve<IdentityMigrator>();

        // Make sure tables do not exist before migration
        foreach (var tableType in SystemDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync<ScopedSystemConnectionFactory>(table.TableName);
            Assert.That(tableExists, Is.False);
        }
        foreach (var tableType in IdentityDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync<ScopedIdentityConnectionFactory>(table.TableName);
            Assert.That(tableExists, Is.False);
        }

        await systemMigrator.MigrateAsync();
        await identityMigrator.MigrateAsync();

        // Make sure tables exist after migration
        foreach (var tableType in SystemDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync<ScopedSystemConnectionFactory>(table.TableName);
            Assert.That(tableExists, Is.True, $"Table {table.TableName} does not exist after migration.");
        }
        foreach (var tableType in IdentityDatabase.TableTypes)
        {
            var table = (TableBase)scope.Resolve(tableType);
            var tableExists = await TableExistsAsync<ScopedIdentityConnectionFactory>(table.TableName);
            Assert.That(tableExists, Is.True, $"Table {table.TableName} does not exist after migration.");
        }
    }

    //
    
    [Test]
    public async Task ItShouldGroupMigrationsCorrectly()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var logger = scope.Resolve<ILogger<IdentityMigrator>>();
        var scopedConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        var nodeLock = scope.Resolve<INodeLock>();

        // NOTE we use a mock, so we can create a new, empty SortedMigrations list
        var mock = new Mock<IdentityMigrator>(logger, scopedConnectionFactory, nodeLock)
        {
            CallBase = true
        };
        mock.Setup(x => x.SortedMigrations).Returns([
            new SomeMigration(-1, 0),
            new SomeMigration(-1, 0),
            new SomeMigration(-1, 0),
            new SomeMigration(0, 1),
            new SomeMigration(0, 1),
            new SomeMigration(1, 2),
        ]);

        var migrator  = mock.Object;

        //
        // UP
        //

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Up, -1, 1000);
            Assert.That(groupedMigrations.Count, Is.EqualTo(3));
            Assert.That(groupedMigrations[0].Key, Is.EqualTo(0));
            Assert.That(groupedMigrations[0].Value.Count, Is.EqualTo(3));
            Assert.That(groupedMigrations[0].Value[0].MigrationVersion, Is.EqualTo(0));
            Assert.That(groupedMigrations[0].Value[1].MigrationVersion, Is.EqualTo(0));
            Assert.That(groupedMigrations[0].Value[2].MigrationVersion, Is.EqualTo(0));
            Assert.That(groupedMigrations[1].Key, Is.EqualTo(1));
            Assert.That(groupedMigrations[1].Value.Count, Is.EqualTo(2));
            Assert.That(groupedMigrations[1].Value[0].MigrationVersion, Is.EqualTo(1));
            Assert.That(groupedMigrations[1].Value[1].MigrationVersion, Is.EqualTo(1));
            Assert.That(groupedMigrations[2].Key, Is.EqualTo(2));
            Assert.That(groupedMigrations[2].Value.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[2].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Up, 1, 1000);
            Assert.That(groupedMigrations.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[0].Key, Is.EqualTo(2));
            Assert.That(groupedMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Up, 10, 1000);
            Assert.That(groupedMigrations.Count, Is.EqualTo(0));
        }

        //
        // DOWN
        //

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Down, 100, 10);
            Assert.That(groupedMigrations.Count, Is.EqualTo(0));
        }

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Down, 100, 1);
            Assert.That(groupedMigrations.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[0].Key, Is.EqualTo(2));
            Assert.That(groupedMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var groupedMigrations = migrator.GroupMigrationsByVersion(AbstractMigrator.Direction.Down, 100, -1);
            Assert.That(groupedMigrations.Count, Is.EqualTo(3));
            Assert.That(groupedMigrations[0].Key, Is.EqualTo(2));
            Assert.That(groupedMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(groupedMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
            Assert.That(groupedMigrations[1].Key, Is.EqualTo(1));
            Assert.That(groupedMigrations[1].Value.Count, Is.EqualTo(2));
            Assert.That(groupedMigrations[1].Value[0].MigrationVersion, Is.EqualTo(1));
            Assert.That(groupedMigrations[1].Value[1].MigrationVersion, Is.EqualTo(1));
            Assert.That(groupedMigrations[2].Value.Count, Is.EqualTo(3));
            Assert.That(groupedMigrations[2].Value[0].MigrationVersion, Is.EqualTo(0));
            Assert.That(groupedMigrations[2].Value[1].MigrationVersion, Is.EqualTo(0));
            Assert.That(groupedMigrations[2].Value[2].MigrationVersion, Is.EqualTo(0));
        }
    }
}

class SomeMigration(long previousVersion, long version) : MigrationBase(previousVersion)
{
    public override long MigrationVersion => version;
    public override Task CreateTableWithCommentAsync(IConnectionWrapper cn)
    {
        return Task.CompletedTask;
    }

    public override Task DownAsync(IConnectionWrapper cn)
    {
        return Task.CompletedTask;
    }

    public override Task UpAsync(IConnectionWrapper cn)
    {
        return Task.CompletedTask;
    }
}


