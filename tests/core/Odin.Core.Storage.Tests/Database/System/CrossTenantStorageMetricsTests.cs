using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.System;

public class CrossTenantStorageMetricsTests : IocTestBase
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
        var metrics = scope.Resolve<CrossTenantStorageMetrics>();

        Assert.That(metrics.IsSupported, Is.EqualTo(databaseType == DatabaseType.Postgres));

        if (!metrics.IsSupported)
        {
            // SQLite gives each tenant its own database file, so there is nothing to group over.
            Assert.That(await metrics.GetAllIdentityStorageAsync(), Is.Empty);
        }
    }

#if RUN_POSTGRES_TESTS
    /// <summary>
    /// The whole point of the cross-tenant scan: it must see identities that the caller never
    /// knew about, including ones with no Registrations row.
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Postgres)]
    public async Task ItShouldReportEveryIdentityWithRows(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();

        // IdentityId is the tenant this scope is wired for; the other two are strangers.
        var orphan1 = Guid.NewGuid();
        var orphan2 = Guid.NewGuid();

        await InsertFileRowAsync(scope, IdentityId, byteCount: 100, fileState: 1);
        await InsertFileRowAsync(scope, IdentityId, byteCount: 40, fileState: 0);
        await InsertFileRowAsync(scope, orphan1, byteCount: 7, fileState: 1);
        await InsertFileRowAsync(scope, orphan2, byteCount: 9, fileState: 0);

        var metrics = scope.Resolve<CrossTenantStorageMetrics>();
        var rows = await metrics.GetAllIdentityStorageAsync();

        Assert.That(rows.Select(r => r.IdentityId),
            Is.EquivalentTo(new[] { IdentityId, orphan1, orphan2 }));

        var mine = rows.Single(r => r.IdentityId == IdentityId);
        Assert.That(mine.Files, Is.EqualTo(2));
        Assert.That(mine.TotalBytes, Is.EqualTo(140));
        Assert.That(mine.ActiveBytes, Is.EqualTo(100));

        var tombstoneOnly = rows.Single(r => r.IdentityId == orphan2);
        Assert.That(tombstoneOnly.TotalBytes, Is.EqualTo(9));
        Assert.That(tombstoneOnly.ActiveBytes, Is.EqualTo(0));
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
