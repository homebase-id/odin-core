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
    public async Task ItShouldGroupMigrationsCorrectlyOnEmptyList()
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
        mock.Setup(x => x.SortedMigrations).Returns([]);

        var migrator  = mock.Object;
        var groupedMigrations = migrator.GroupMigrationsByVersion();

        Assert.That(groupedMigrations.Count, Is.EqualTo(0));
    }

    //

    [Test]
    public async Task ItShouldGroupMigrationsCorrectlyOnNonEmptyList()
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
            new SomeMigration(0, 1),
            new SomeMigration(-1, 0),
            new SomeMigration(0, 1),
            new SomeMigration(1, 2),
            new SomeMigration(-1, 0),
        ]);

        var migrator  = mock.Object;
        var groupedMigrations = migrator.GroupMigrationsByVersion();

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

    //
    
    [Test]
    public async Task ItShouldFilterMigrationsCorrectly()
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
        var groupedMigrations = migrator.GroupMigrationsByVersion();

        //
        // UP
        //

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Up, -1, 1000);
            Assert.That(filteredMigrations.Count, Is.EqualTo(3));
            Assert.That(filteredMigrations[0].Key, Is.EqualTo(0));
            Assert.That(filteredMigrations[0].Value.Count, Is.EqualTo(3));
            Assert.That(filteredMigrations[0].Value[0].MigrationVersion, Is.EqualTo(0));
            Assert.That(filteredMigrations[0].Value[1].MigrationVersion, Is.EqualTo(0));
            Assert.That(filteredMigrations[0].Value[2].MigrationVersion, Is.EqualTo(0));
            Assert.That(filteredMigrations[1].Key, Is.EqualTo(1));
            Assert.That(filteredMigrations[1].Value.Count, Is.EqualTo(2));
            Assert.That(filteredMigrations[1].Value[0].MigrationVersion, Is.EqualTo(1));
            Assert.That(filteredMigrations[1].Value[1].MigrationVersion, Is.EqualTo(1));
            Assert.That(filteredMigrations[2].Key, Is.EqualTo(2));
            Assert.That(filteredMigrations[2].Value.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[2].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Up, 1, 1000);
            Assert.That(filteredMigrations.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[0].Key, Is.EqualTo(2));
            Assert.That(filteredMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Up, 10, 1000);
            Assert.That(filteredMigrations.Count, Is.EqualTo(0));
        }

        //
        // DOWN
        //

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Down, 100, 10);
            Assert.That(filteredMigrations.Count, Is.EqualTo(0));
        }

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Down, 100, 1);
            Assert.That(filteredMigrations.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[0].Key, Is.EqualTo(2));
            Assert.That(filteredMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
        }

        {
            var filteredMigrations = migrator.FilterMigrationsByVersion(groupedMigrations, AbstractMigrator.Direction.Down, 100, -1);
            Assert.That(filteredMigrations.Count, Is.EqualTo(3));
            Assert.That(filteredMigrations[0].Key, Is.EqualTo(2));
            Assert.That(filteredMigrations[0].Value.Count, Is.EqualTo(1));
            Assert.That(filteredMigrations[0].Value[0].MigrationVersion, Is.EqualTo(2));
            Assert.That(filteredMigrations[1].Key, Is.EqualTo(1));
            Assert.That(filteredMigrations[1].Value.Count, Is.EqualTo(2));
            Assert.That(filteredMigrations[1].Value[0].MigrationVersion, Is.EqualTo(1));
            Assert.That(filteredMigrations[1].Value[1].MigrationVersion, Is.EqualTo(1));
            Assert.That(filteredMigrations[2].Value.Count, Is.EqualTo(3));
            Assert.That(filteredMigrations[2].Value[0].MigrationVersion, Is.EqualTo(0));
            Assert.That(filteredMigrations[2].Value[1].MigrationVersion, Is.EqualTo(0));
            Assert.That(filteredMigrations[2].Value[2].MigrationVersion, Is.EqualTo(0));
        }
    }

    //

    [Test]
    public async Task WhatGoesUpMustComeDownNoDuplicates()
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
            new SomeMigration(0, 1),
            new SomeMigration(1, 2),
            new SomeMigration(2, 300),
        ]);

        var migrator = mock.Object;

        SomeMigration.Step = 0;

        //
        // Migrate all the way up
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(4));
        }

        //
        // Migrate all the way up again (nothing to do)
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(4));
        }

        //
        // Migrate to version 0
        //
        {
            await migrator.MigrateAsync(0);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(0));
            Assert.That(SomeMigration.Step, Is.EqualTo(7));
        }

        //
        // Migrate to version 1
        //
        {
            await migrator.MigrateAsync(1);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(1));
            Assert.That(SomeMigration.Step, Is.EqualTo(8));
        }

        //
        // Migrate to version 2
        //
        {
            await migrator.MigrateAsync(2);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(2));
            Assert.That(SomeMigration.Step, Is.EqualTo(9));
        }

        //
        // Migrate to version 1
        //
        {
            await migrator.MigrateAsync(1);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(1));
            Assert.That(SomeMigration.Step, Is.EqualTo(10));
        }

        //
        // Migrate to MAX
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(12));
        }

        //
        // Migrate to MIN
        //
        {
            await migrator.MigrateAsync(-1);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(0));
            Assert.That(SomeMigration.Step, Is.EqualTo(15));
        }

        //
        // Throw on unknown version
        //
        {
            var exception = Assert.ThrowsAsync<MigrationException>(async () => await migrator.MigrateAsync(9999));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message,
                Does.Contain("Requested migration version 9999 does not exist in the migration set."));
        }
    }

    //

    [Test]
    public async Task WhatGoesUpMustComeDownWithDuplicates()
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
            new SomeMigration(0, 1),
            new SomeMigration(0, 1),
            new SomeMigration(1, 2),
            new SomeMigration(1, 2),
            new SomeMigration(1, 2),
            new SomeMigration(2, 300),
            new SomeMigration(2, 300),
        ]);

        var migrator = mock.Object;

        SomeMigration.Step = 0;

        //
        // Migrate all the way up
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(9));
        }

        //
        // Migrate all the way up again (nothing to do)
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(9));
        }

        //
        // Migrate to version 0
        //
        {
            await migrator.MigrateAsync(0);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(0));
            Assert.That(SomeMigration.Step, Is.EqualTo(16));
        }

        //
        // Migrate to version 1
        //
        {
            await migrator.MigrateAsync(1);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(1));
            Assert.That(SomeMigration.Step, Is.EqualTo(18));
        }

        //
        // Migrate to version 2
        //
        {
            await migrator.MigrateAsync(2);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(2));
            Assert.That(SomeMigration.Step, Is.EqualTo(21));
        }

        //
        // Migrate to MAX
        //
        {
            await migrator.MigrateAsync();
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(300));
            Assert.That(SomeMigration.Step, Is.EqualTo(23));
        }

        //
        // Migrate to MIN
        //
        {
            await migrator.MigrateAsync(-1);
            var currentVersion = await migrator.GetCurrentVersionAsync();
            Assert.That(currentVersion, Is.EqualTo(0));
            Assert.That(SomeMigration.Step, Is.EqualTo(30));
        }

        //
        // Throw on unknown version
        //
        {
            var exception = Assert.ThrowsAsync<MigrationException>(async () => await migrator.MigrateAsync(9999));
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message,
                Does.Contain("Requested migration version 9999 does not exist in the migration set."));
        }
    }


}



//

class SomeMigration(long previousVersion, long version) : MigrationBase(previousVersion)
{
    public static int Step = 0;
    public override long MigrationVersion => version;
    public override Task CreateTableWithCommentAsync(IConnectionWrapper cn)
    {
        return Task.CompletedTask;
    }

    public override Task DownAsync(IConnectionWrapper cn)
    {
        Step++;
        return Task.CompletedTask;
    }

    public override Task UpAsync(IConnectionWrapper cn)
    {
        Step++;
        return Task.CompletedTask;
    }
}


