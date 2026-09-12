using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.System;

public class IdentityStorageCensusTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task IsSupportedOnlyOnPostgres(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var census = scope.Resolve<IdentityStorageCensus>();

        Assert.That(census.IsSupported, Is.EqualTo(databaseType == DatabaseType.Postgres));

        if (!census.IsSupported)
        {
            // SQLite gives each tenant its own database file: an orphaned file is not reachable
            // from any other, so there is nothing to group over.
            Assert.That(await census.GetAllAsync(), Is.Empty);
        }
    }

#if RUN_POSTGRES_TESTS
    /// <summary>
    /// The defect this exists for: an identity deleted from Registrations leaves its rows behind,
    /// and no per-identity SUM can find it, because the identity is no longer in the registry to
    /// be asked about. The census starts from the data, so it sees both.
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldCountRegisteredAndUnregisteredIdentitiesAlike(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();

        // IdentityId is still registered; the other two are the residue of deleted tenants.
        var orphan1 = Guid.NewGuid();
        var orphan2 = Guid.NewGuid();

        await InsertFileRowAsync(scope, IdentityId, byteCount: 100, fileState: 1);
        await InsertFileRowAsync(scope, IdentityId, byteCount: 40, fileState: 0);
        await InsertFileRowAsync(scope, orphan1, byteCount: 7, fileState: 1);
        await InsertFileRowAsync(scope, orphan2, byteCount: 9, fileState: 0);

        var census = scope.Resolve<IdentityStorageCensus>();
        var rows = await census.GetAllAsync();

        Assert.That(rows.Select(r => r.IdentityId),
            Is.EquivalentTo(new[] { IdentityId, orphan1, orphan2 }));

        // The registered tenant's figures come from the same pass, so the endpoint does not need
        // a separate query per tenant.
        var registered = rows.Single(r => r.IdentityId == IdentityId);
        Assert.That(registered.Files, Is.EqualTo(2));
        Assert.That(registered.TotalBytes, Is.EqualTo(140));
        Assert.That(registered.ActiveBytes, Is.EqualTo(100));

        var live = rows.Single(r => r.IdentityId == orphan1);
        Assert.That(live.TotalBytes, Is.EqualTo(7));
        Assert.That(live.ActiveBytes, Is.EqualTo(7));

        // Left behind as tombstones only: still occupying space, nothing restorable.
        var tombstoneOnly = rows.Single(r => r.IdentityId == orphan2);
        Assert.That(tombstoneOnly.TotalBytes, Is.EqualTo(9));
        Assert.That(tombstoneOnly.ActiveBytes, Is.EqualTo(0));
    }

    /// <summary>
    /// The census must agree with the per-identity sum the endpoint falls back to on SQLite,
    /// or the two backends would report the same tenant differently.
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldAgreeWithThePerIdentitySum(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();

        await InsertFileRowAsync(scope, IdentityId, byteCount: 100, fileState: 1);
        await InsertFileRowAsync(scope, IdentityId, byteCount: 40, fileState: 0);
        await InsertFileRowAsync(scope, Guid.NewGuid(), byteCount: 7, fileState: 1);

        var fromCensus = (await scope.Resolve<IdentityStorageCensus>().GetAllAsync())
            .Single(r => r.IdentityId == IdentityId);
        var fromSum = await scope.Resolve<Odin.Core.Storage.Database.Identity.Table.TableDriveMainIndex>()
            .GetIdentityStorageStatsAsync();

        Assert.That(fromCensus.Files, Is.EqualTo(fromSum.Files));
        Assert.That(fromCensus.TotalBytes, Is.EqualTo(fromSum.TotalBytes));
        Assert.That(fromCensus.ActiveBytes, Is.EqualTo(fromSum.ActiveBytes));
    }
#endif

    private static async Task InsertFileRowAsync(
        ILifetimeScope scope, Guid identityId, long byteCount, int fileState)
    {
        var factory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await factory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            """
            INSERT INTO drivemainindex
                (identityId,driveId,fileId,fileState,requiredSecurityGroup,fileSystemType,
                 userDate,fileType,dataType,archivalStatus,historyStatus,byteCount,
                 hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrServerData,hdrFileMetaData,
                 hdrTmpDriveAlias,hdrTmpDriveType,created,modified)
            VALUES (@identityId,@driveId,@fileId,@fileState,0,0,
                    0,0,0,0,0,@byteCount,
                    '{}',@hdrVersionTag,'{}','{}','{}',
                    @driveId,@driveId,0,0);
            """;

        cmd.AddParameter("@identityId", DbType.Binary, identityId);
        cmd.AddParameter("@driveId", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@fileId", DbType.Binary, Guid.NewGuid());
        cmd.AddParameter("@fileState", DbType.Int32, fileState);
        cmd.AddParameter("@byteCount", DbType.Int64, byteCount);
        cmd.AddParameter("@hdrVersionTag", DbType.Binary, Guid.NewGuid());

        await cmd.ExecuteNonQueryAsync();
    }
}
