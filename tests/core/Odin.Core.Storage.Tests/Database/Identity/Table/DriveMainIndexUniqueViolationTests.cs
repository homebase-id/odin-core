using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Npgsql;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

/// <summary>
/// <see cref="OdinDatabaseException.IsUniqueConstraintViolationOn"/> names the index a drive file's row broke from the
/// database's own error, inside the transaction a new file's commit runs in, where Postgres refuses any read after the
/// failed write (#2085)
/// </summary>
public class DriveMainIndexUniqueViolationTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task AGlobalTransitIdClashIsNamedAsOne(DatabaseType databaseType)
    {
        var gtid = Guid.NewGuid();
        var e = await ClashAsync(databaseType, first: (gtid, null), second: (gtid, null));

        Assert.That(e.IsUniqueConstraintViolationOn(GlobalTransitId), Is.True, e.InnerException?.Message);
        Assert.That(e.IsUniqueConstraintViolationOn("uniqueid"), Is.False, e.InnerException?.Message);
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task AUniqueIdClashIsNotNamedAsAGlobalTransitIdOne(DatabaseType databaseType)
    {
        var uniqueId = Guid.NewGuid();
        var e = await ClashAsync(databaseType, first: (Guid.NewGuid(), uniqueId), second: (Guid.NewGuid(), uniqueId));

        Assert.That(e.IsUniqueConstraintViolationOn("uniqueid"), Is.True, e.InnerException?.Message);
        Assert.That(e.IsUniqueConstraintViolationOn(GlobalTransitId), Is.False, e.InnerException?.Message);
    }

    /// <summary>
    /// The names Postgres 16 gave DriveMainIndex's unique constraints: on the table as created, on a migration's table,
    /// and on later migrations' tables made while an earlier one's, kept under its old name, held the shorter names.
    /// Only the global transit id ones may answer to the marker, however Postgres cut them.
    /// </summary>
    [TestCase("drivemainindex_identityid_driveid_globaltransitid_key", true)]
    [TestCase("drivemainindexmigrationsv0_identityid_driveid_globaltransit_key", true)]
    [TestCase("drivemainindexmigrationsv2026_identityid_driveid_globaltran_key", true)]
    [TestCase("drivemainindexmigrationsv2026_identityid_driveid_globaltra_key1", true)]
    [TestCase("drivemainindexmigrationsv2026_identityid_driveid_globaltra_key2", true)]
    [TestCase("drivemainindexmigrationsv202611_identityid_driveid_uniqueid_key", false)]
    [TestCase("drivemainindexmigrationsv20261_identityid_driveid_uniqueid_key1", false)]
    [TestCase("drivemainindexmigrationsv20261117_identityid_driveid_fileid_key", false)]
    [TestCase("drivemainindexmigrationsv202611171_identityid_hdrversiontag_key", false)]
    [TestCase("drivemainindex_pkey", false)]
    public void APostgresConstraintNameAnswersToTheMarkerOnlyOnTheGlobalTransitId(string constraintName, bool isGlobalTransitId)
    {
        var e = new OdinDatabaseException(DatabaseType.Postgres, "a clash",
            new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505",
                constraintName: constraintName));

        Assert.That(e.IsUniqueConstraintViolationOn(GlobalTransitId), Is.EqualTo(isGlobalTransitId));
    }

    private const string GlobalTransitId = TableDriveMainIndex.GlobalTransitIdIndexMarker;

    /// <summary>Two new files on one drive, the second written in a transaction as a commit is, and the error it gets</summary>
    private async Task<OdinDatabaseException> ClashAsync(DatabaseType databaseType, (Guid gtid, Guid? uniqueId) first,
        (Guid gtid, Guid? uniqueId) second)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();
        var metaIndex = scope.Resolve<MainIndexMeta>();
        var driveId = Guid.NewGuid();

        await AddAsync(metaIndex, driveId, first);

        await using var tx = await db.BeginStackedTransactionAsync();
        var e = Assert.ThrowsAsync<OdinDatabaseException>(() => AddAsync(metaIndex, driveId, second));
        Assert.That(e!.IsUniqueConstraintViolation, Is.True, e.Message);
        return e;
    }

    private static Task AddAsync(MainIndexMeta metaIndex, Guid driveId, (Guid gtid, Guid? uniqueId) file) =>
        metaIndex.TestAddEntryPassalongToUpsertAsync(driveId, Guid.NewGuid(), file.gtid, 1, 1, "sender", null, file.uniqueId, 0,
            new UnixTimeUtc(0), 0, null, null, 1);
}
