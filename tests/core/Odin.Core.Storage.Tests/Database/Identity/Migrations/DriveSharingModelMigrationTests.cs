using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Migrations;
using Odin.Core.Storage.Factory;
using Odin.Services.Drives;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

/// <summary>
/// <see cref="TableDrivesMigrationV202610221500"/> adds the zero-knowledge columns, makes the master-key
/// column nullable, and leaves every drive Collaborative.  A drive turns Private only when the browser
/// converts it, so a label never claims zero knowledge the drive doesn't have.
/// </summary>
public class DriveSharingModelMigrationTests : IocTestBase
{
    private const long VersionBeforeSharingModel = 202610140701;

    private static readonly Guid VaultDrive = WellKnownAppDrives.VaultDrive.Alias.Value;
    private static readonly Guid ShardRecoveryDrive = WellKnownAppDrives.ShardRecoveryDrive.Alias.Value;

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task EveryDriveStaysCollaborative(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync(VersionBeforeSharingModel);
        var connectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();

        var appDrive = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, VaultDrive, "vault");
        await InsertAsync(connectionFactory, IdentityId, ShardRecoveryDrive, "shard-recovery");
        await InsertAsync(connectionFactory, IdentityId, appDrive, "app-drive");

        // A second identity in the same table: on Postgres every identity shares it and it is migrated once.
        var otherIdentity = Guid.NewGuid();
        await InsertAsync(connectionFactory, otherIdentity, VaultDrive, "vault");

        await migrator.MigrateAsync();

        Assert.That(await migrator.GetCurrentVersionAsync(), Is.GreaterThanOrEqualTo(202610221500));

        var rows = await ReadAsync(connectionFactory);
        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows.Values.Select(r => r.SharingModel), Is.All.EqualTo((int)DriveSharingModel.Collaborative),
            "no drive is labelled Private before it is converted");
        Assert.That(rows.Values.Select(r => r.ZkKey), Is.All.Null, "no drive is zero knowledge until the browser converts it");
        Assert.That(rows.Values.Select(r => r.MasterKeyJson), Is.All.EqualTo("{}"), "the master-key copy is carried over");

        // The master-key column now takes NULL, as a converted drive needs.
        await SetMasterKeyNullAsync(connectionFactory, IdentityId, VaultDrive);
        Assert.That((await ReadAsync(connectionFactory))[(IdentityId, VaultDrive)].MasterKeyJson, Is.Null);
    }

    private static async Task InsertAsync(ScopedIdentityConnectionFactory connectionFactory, Guid identityId,
        Guid driveId, string driveSlug)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO Drives (identityId,DriveId,StorageKeyCheckValue,DriveType,DriveName," +
            "MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,AppId,DriveSlug," +
            "DriveTypeSlug,created,modified) VALUES (@identityId,@driveId,@check,@driveType,@name,'{}','iv','id','{}'," +
            "@appId,@driveSlug,'drive',1,1);";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        cmd.AddParameter("@check", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@driveType", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@name", DbType.String, driveSlug);
        cmd.AddParameter("@appId", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@driveSlug", DbType.String, driveSlug);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SetMasterKeyNullAsync(ScopedIdentityConnectionFactory connectionFactory, Guid identityId,
        Guid driveId)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE Drives SET MasterKeyEncryptedStorageKeyJson=NULL WHERE identityId=@identityId AND DriveId=@driveId;";
        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@driveId", DbType.Binary, driveId);
        Assert.That(await cmd.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    private static async Task<Dictionary<(Guid IdentityId, Guid DriveId), (long SharingModel, string MasterKeyJson, string ZkKey)>>
        ReadAsync(ScopedIdentityConnectionFactory connectionFactory)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText =
            "SELECT identityId,DriveId,SharingModel,MasterKeyEncryptedStorageKeyJson,ZkMasterKeyEncryptedStorageKey FROM Drives;";
        var result = new Dictionary<(Guid, Guid), (long, string, string)>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result[(new Guid((byte[])rdr[0]), new Guid((byte[])rdr[1]))] = (
                Convert.ToInt64(rdr[2]),
                rdr[3] == DBNull.Value ? null : (string)rdr[3],
                rdr[4] == DBNull.Value ? null : (string)rdr[4]);
        }

        return result;
    }
}
