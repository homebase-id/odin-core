using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Migrations;

namespace Odin.Core.Storage.Tests.Database.Identity;

public class DatabaseMigrationTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task IdempotentTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        // I need to upgrade this code when I am not stress coding.
        var list = new TableDriveMainIndexMigrationList();
        var latest = list.GetLatestVersion();

        // Check it's the latest version
        var metaIndex = scope.Resolve<TableDriveMainIndex>();
        var sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        ClassicAssert.IsTrue(sqlVersion == latest.MigrationVersion);

        var previous = list.Migrations[list.Migrations.Count - 2];
        await previous.CreateTableWithCommentAsync(cn);

        // Check it's STILL the latest version - must be idempotent
        sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        ClassicAssert.IsTrue(sqlVersion == latest.MigrationVersion);
    }


    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task TableCommentTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        // I need to upgrade this code when I am not stress coding.
        var list = new TableDriveMainIndexMigrationList();
        var m1 = list.GetByVersion(0);
        string version = await SqlHelper.GetTableCommentAsync(cn, "Drives");
        var v = await SqlHelper.GetTableVersionAsync(cn, "Drives");
        ClassicAssert.IsTrue(v == 0);
    }
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task MigrationTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        var list = new TableDriveMainIndexMigrationList();
        list.Validate();

        // Let's just be sure that the table in the database in the current one
        // This will be wrong when we get a newer table - I will rewrite it when I am not 
        // stress coding
        var sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        var latest = list.GetLatestVersion();
        ClassicAssert.IsTrue(sqlVersion == latest.MigrationVersion);

        var previous = list.Migrations[list.Migrations.Count - 2];
        ClassicAssert.IsTrue(latest.PreviousVersion == previous.MigrationVersion);

        // We need to downgrade to the previous version
        await SqlHelper.DeleteTableAsync(cn, "DriveMainIndex");
        await previous.CreateTableWithCommentAsync(cn);
        await SqlHelper.RenameAsync(cn, $"DriveMainIndexMigrationsV{previous.MigrationVersion}", "DriveMainIndex");

        sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        ClassicAssert.IsTrue(sqlVersion == previous.MigrationVersion);

        // Fill in some random data
        var metaIndex = scope.Resolve<MainIndexMeta>();
        var driveId = Guid.NewGuid();

        var f1 = SequentialGuid.CreateGuid(); // Oldest
        var s1 = SequentialGuid.CreateGuid().ToString();
        var t1 = SequentialGuid.CreateGuid();
        var f2 = SequentialGuid.CreateGuid();
        var f3 = SequentialGuid.CreateGuid(); // Newest

        // This API does not match the table version, but since they are the same, no worries
        await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(1000), 0, null, null, 1);
        await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(42), 1, null, null, 1);
        await metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(2000), 2, null, null, 1);

        await latest.UpAsync(cn);   // Increase from version 0 to 20250719
        sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        ClassicAssert.IsTrue(sqlVersion == latest.MigrationVersion);

        await latest.DownAsync(cn); // Rollback version 20250719 back to 0
        sqlVersion = await SqlHelper.GetTableVersionAsync(cn, "DriveMainIndex");
        ClassicAssert.IsTrue(sqlVersion == latest.PreviousVersion);
    }
}