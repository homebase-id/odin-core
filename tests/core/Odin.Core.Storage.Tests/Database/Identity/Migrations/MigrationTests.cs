using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using System;
using System.Data;

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Migrations;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

public class DatabaseMigrationTests : IocTestBase
{
    [Test]
    public async Task GlobalMigrationListTest()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();

        long prev = -1;
        foreach (var m in migrator.SortedMigrations)
        {
            ClassicAssert.IsTrue(m.MigrationVersion >= prev);
            prev = m.MigrationVersion;
        }            
    }


    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    /// <summary>
    /// Making sure that running Create table on an already created table doesn't affect the 
    /// table's embedded version number (much harder than it sounds)
    /// </summary>
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

        var commentJson = await SqlHelper.GetTableCommentAsync(cn, "Drives");
        var doc = JsonDocument.Parse(commentJson);

        var commentVersion = doc.RootElement.GetProperty("Version").GetInt64();
        var tableVersion = await SqlHelper.GetTableVersionAsync(cn, "Drives");

        Assert.That(commentVersion, Is.EqualTo(tableVersion));
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

    /// <summary>
    /// Rolls a table back to its previous version, puts a real row in it, then migrates up.
    /// This is what exercises CopyDataAsync - the generated copy names every column on both
    /// sides, so any migration that adds a column has to be hand-edited to leave the new ones
    /// out. An empty table would pass regardless; only a populated one catches the mistake.
    /// </summary>
    private static async Task AssertMigratesRowUpAndDownAsync(
        IConnectionWrapper cn,
        MigrationListBase list,
        string tableName,
        string insertPreviousVersionRowSql,
        Action<ICommandWrapper> bindInsertParameters,
        string assertAfterUpSql)
    {
        var latest = list.GetLatestVersion();
        var previous = list.Migrations[list.Migrations.Count - 2];
        Assert.That(latest.PreviousVersion, Is.EqualTo(previous.MigrationVersion),
            $"{tableName}: latest migration does not chain to the previous one");

        // Roll the live table back to the previous version
        await SqlHelper.DeleteTableAsync(cn, tableName);
        await previous.CreateTableWithCommentAsync(cn);
        await SqlHelper.RenameAsync(cn, $"{tableName}MigrationsV{previous.MigrationVersion}", tableName);
        Assert.That(await SqlHelper.GetTableVersionAsync(cn, tableName), Is.EqualTo(previous.MigrationVersion));

        // Raw SQL on purpose: the generated CRUD writes the *new* column set and cannot
        // insert into the old shape. Parameterised so the same statement runs on both dialects
        // (BYTEA literals differ between SQLite and Postgres).
        await using (var insert = cn.CreateCommand())
        {
            insert.CommandText = insertPreviousVersionRowSql;
            bindInsertParameters(insert);
            Assert.That(await insert.ExecuteNonQueryAsync(), Is.EqualTo(1));
        }

        await latest.UpAsync(cn);
        Assert.That(await SqlHelper.GetTableVersionAsync(cn, tableName), Is.EqualTo(latest.MigrationVersion));

        // The row survived, and the new columns carry their intended NULL / DEFAULT values
        await using (var verify = cn.CreateCommand())
        {
            verify.CommandText = assertAfterUpSql;
            var matched = Convert.ToInt64(await verify.ExecuteScalarAsync());
            Assert.That(matched, Is.EqualTo(1),
                $"{tableName}: row did not survive the migration with the expected new-column values");
        }

        await latest.DownAsync(cn);
        Assert.That(await SqlHelper.GetTableVersionAsync(cn, tableName), Is.EqualTo(latest.PreviousVersion));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task DrivesMigration_CopiesExistingRow_AndDefaultsNewColumnsToNull(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        await AssertMigratesRowUpAndDownAsync(cn, new TableDrivesMigrationList(), "Drives",
            "INSERT INTO Drives (identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName," +
            "MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
            "VALUES (@identityId,@driveId,@storageKeyCheckValue,@driveType,'seed drive','{}','iv','value','{}',1,1)",
            cmd =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, IdentityId.ToByteArray());
                cmd.AddParameter("@driveId", DbType.Binary, Guid.NewGuid().ToByteArray());
                cmd.AddParameter("@storageKeyCheckValue", DbType.Binary, Guid.NewGuid().ToByteArray());
                cmd.AddParameter("@driveType", DbType.Binary, Guid.NewGuid().ToByteArray());
            },
            // AppId / DriveSlug / DriveTypeSlug / WriteOnlyKeyPair are nullable and must land NULL:
            // an existing drive is not app-owned, has no slug, and has deposits disabled.
            "SELECT COUNT(*) FROM Drives WHERE DriveName = 'seed drive' " +
            "AND AppId IS NULL AND DriveSlug IS NULL AND DriveTypeSlug IS NULL AND WriteOnlyKeyPair IS NULL");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task CircleMigration_CopiesExistingRow_AndAppliesColumnDefaults(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        await AssertMigratesRowUpAndDownAsync(cn, new TableCircleMigrationList(), "Circle",
            "INSERT INTO Circle (identityId,circleId,circleName,data) " +
            "VALUES (@identityId,@circleId,'seed circle',NULL)",
            cmd =>
            {
                cmd.AddParameter("@identityId", DbType.Binary, IdentityId.ToByteArray());
                cmd.AddParameter("@circleId", DbType.Binary, Guid.NewGuid().ToByteArray());
            },
            // GrantOn/Designation are NOT NULL and take their column DEFAULTs of
            // 0 (NONE) and 1 (PERSONAL), which preserve today's behaviour.
            "SELECT COUNT(*) FROM Circle WHERE circleName = 'seed circle' " +
            "AND AppId IS NULL AND Emoji IS NULL AND GrantOn = 0 AND Designation = 1");
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ConnectionsMigration_CopiesExistingRow_AndLeavesReviewedAtNull(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();

        await AssertMigratesRowUpAndDownAsync(cn, new TableConnectionsMigrationList(), "Connections",
            "INSERT INTO Connections (identityId,identity,displayName,status,accessIsRevoked,data,created,modified) " +
            "VALUES (@identityId,'frodo.dotyou.cloud','Frodo',1,0,NULL,1,1)",
            cmd => cmd.AddParameter("@identityId", DbType.Binary, IdentityId.ToByteArray()),
            // NULL ReviewedAt means "New / not yet reviewed" for every pre-existing connection.
            "SELECT COUNT(*) FROM Connections WHERE identity = 'frodo.dotyou.cloud' AND ReviewedAt IS NULL");
    }
}