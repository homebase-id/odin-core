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
/// column nullable, and labels the built-in drives with their sharing model.
/// </summary>
public class DriveSharingModelMigrationTests : IocTestBase
{
    private const long VersionBeforeSharingModel = 202610140701;

    private static readonly Guid VaultDrive = WellKnownAppDrives.VaultDrive.Alias.Value;
    private static readonly Guid PhotoLibraryDrive = WellKnownAppDrives.PhotoLibraryDrive.Alias.Value;
    private static readonly Guid TransientTempDrive = SystemDriveConstants.TransientTempDrive.Alias.Value;
    private static readonly Guid CommunityDrive = WellKnownAppDrives.CommunityDrive.Alias.Value;

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task BuiltinDrivesAreLabelledAndEveryOtherDriveStaysCollaborative(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync(VersionBeforeSharingModel);
        var connectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();

        var appDrive = Guid.NewGuid();
        await InsertAsync(connectionFactory, IdentityId, VaultDrive, "vault");
        await InsertAsync(connectionFactory, IdentityId, PhotoLibraryDrive, "photos");
        await InsertAsync(connectionFactory, IdentityId, TransientTempDrive, "transient");
        await InsertAsync(connectionFactory, IdentityId, CommunityDrive, "community");
        await InsertAsync(connectionFactory, IdentityId, appDrive, "app-drive");

        // A second identity in the same table: on Postgres every identity shares it and it is migrated once.
        var otherIdentity = Guid.NewGuid();
        await InsertAsync(connectionFactory, otherIdentity, VaultDrive, "vault");

        await migrator.MigrateAsync();

        Assert.That(await migrator.GetCurrentVersionAsync(), Is.GreaterThanOrEqualTo(202610221500));

        var rows = await ReadAsync(connectionFactory);
        Assert.That(rows[(IdentityId, VaultDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.Private));
        Assert.That(rows[(IdentityId, PhotoLibraryDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.SelectiveShare));
        Assert.That(rows[(IdentityId, TransientTempDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.Unclassified));
        Assert.That(rows[(IdentityId, CommunityDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.Collaborative));
        Assert.That(rows[(IdentityId, appDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.Collaborative));
        Assert.That(rows[(otherIdentity, VaultDrive)].SharingModel, Is.EqualTo((int)DriveSharingModel.Private));

        Assert.That(rows.Values.Select(r => r.ZkKey), Is.All.Null, "no drive is zero knowledge until the browser converts it");
        Assert.That(rows.Values.Select(r => r.MasterKeyJson), Is.All.EqualTo("{}"), "the master-key copy is carried over");

        // The master-key column now takes NULL, as a converted drive needs.
        await SetMasterKeyNullAsync(connectionFactory, IdentityId, VaultDrive);
        Assert.That((await ReadAsync(connectionFactory))[(IdentityId, VaultDrive)].MasterKeyJson, Is.Null);
    }

    /// <summary>
    /// Existing identities get their labels from the migration's frozen map; new identities from
    /// <c>BuiltinDrives</c>.  The map is pinned to the declarations as they were when it
    /// shipped (2026-10-22), not to today's: a drive added or relabelled later needs its own
    /// migration, and must not change this one.
    /// </summary>
    [Test]
    public void TheMigrationsLabelsAreTheDeclarationsWhenItShipped()
    {
        const int @private = (int)DriveSharingModel.Private;
        const int selectiveShare = (int)DriveSharingModel.SelectiveShare;
        var whenShipped = new Dictionary<Guid, int>
        {
            [Guid.Parse("2612429d1c3f037282b8d42fb2cc0499")] = @private, // contacts
            [Guid.Parse("92bbcad8-3558-417b-9376-9976c086a674")] = @private, // email
            [Guid.Parse("4db49422ebad02e99ab96e9c477d1e08")] = @private, // feed
            [Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac")] = @private, // wallet
            [Guid.Parse("f47ac10b58cc4372a5670e02b2c3d479")] = @private, // vault
            [Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11")] = @private, // chat
            [Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0")] = @private, // mail
            [Guid.Parse("46242d0d67604b2aa683f05cd48d4aef")] = @private, // shard recovery
            [Guid.Parse("6483b7b1f71bd43eb6896c86148668cc")] = selectiveShare, // photo library
            [Guid.Parse("3b9c5f2e-7a41-4d6b-9e0c-8f1a2b3c4d5e")] = selectiveShare, // stickers
            [Guid.Parse("2e191a14-8640-4ebc-b0c8-aaac913f6fa8")] = selectiveShare, // location
            [Guid.Parse("a44e7a2651f44a26ad125d7627b35d0e")] = selectiveShare, // lists
            [Guid.Parse("a85f8562-6c74-4947-896b-619812cafccc")] = selectiveShare, // moments
            [Guid.Parse("ec83345af6a747d4404ef8b0f8844caa")] = selectiveShare, // home page config
            [Guid.Parse("8f12d8c4933813d378488d91ed23b64c")] = selectiveShare, // profile
            [Guid.Parse("6d1711af-8b93-43ef-b798-b84d51f25828")] = selectiveShare, // webdrop
            [Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c")] = (int)DriveSharingModel.Unclassified, // transient temp
        };

        Assert.That(TableDrivesMigrationV202610221500.BuiltinSharingModels, Is.EquivalentTo(whenShipped));
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
