using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Migrations;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

/// <summary>
/// <see cref="TableCircleMigrationV202610301200"/>, <see cref="TableCircleMemberMigrationV202610301200"/> and
/// <see cref="TableDriveAclIndexMigrationV202610301200"/> add the Selective Share columns
/// (docs/selective-share-plan.md).  Every existing row is carried over with them NULL: no circle has a key, no
/// member a sealed copy, no share expires, and every ACL index row means read.
/// </summary>
public class ShareKeyColumnsMigrationTests : IocTestBase
{
    private const long VersionBeforeShareKeys = 202610221500;

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ExistingRowsKeepTheirDataAndGetNoShareKeys(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType, createDatabases: false);
        await using var scope = Services.BeginLifetimeScope();
        var migrator = scope.Resolve<IdentityMigrator>();
        await migrator.MigrateAsync(VersionBeforeShareKeys);
        var connectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();

        var circleId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        await ExecuteAsync(connectionFactory,
            "INSERT INTO Circle (identityId,circleId,circleName,CircleSlug) VALUES (@identityId,@circleId,'Family','family');",
            ("@circleId", circleId));
        await ExecuteAsync(connectionFactory,
            "INSERT INTO CircleMember (identityId,circleId,memberId) VALUES (@identityId,@circleId,@memberId);",
            ("@circleId", circleId), ("@memberId", memberId));
        await ExecuteAsync(connectionFactory,
            "INSERT INTO DriveAclIndex (identityId,driveId,fileId,aclMemberId) VALUES (@identityId,@driveId,@fileId,@circleId);",
            ("@driveId", driveId), ("@fileId", fileId), ("@circleId", circleId));

        await migrator.MigrateAsync();

        Assert.That(await migrator.GetCurrentVersionAsync(), Is.GreaterThanOrEqualTo(202610301200));

        var circles = (await scope.Resolve<TableCircle>().PagingByRowIdAsync(10, null)).Item1;
        var members = (await scope.Resolve<TableCircleMember>().PagingByRowIdAsync(10, null)).Item1;
        var aclRows = (await scope.Resolve<TableDriveAclIndex>().PagingByRowIdAsync(10, null)).Item1;

        Assert.That(circles.Select(c => (c.circleId, c.circleName)), Is.EqualTo(new[] { (circleId, "Family") }));
        Assert.That(members.Select(m => (m.circleId, m.memberId)), Is.EqualTo(new[] { (circleId, memberId) }));
        Assert.That(aclRows.Select(a => (a.driveId, a.fileId, a.aclMemberId)), Is.EqualTo(new[] { (driveId, fileId, circleId) }));

        Assert.That(circles[0].appKeyLockedShareKey, Is.Null, "no circle has a key until its app first shares");
        Assert.That(members[0].sealedShareKey, Is.Null, "no member has a sealed copy until the app seals one");
        Assert.That(aclRows[0].expiresAt, Is.Null, "no existing share expires");
        Assert.That(aclRows[0].permissions, Is.EqualTo(1), "every existing row means read, as it did before");

        // The new columns hold what is written and read back through the generated records.
        var lockedKey = Enumerable.Range(0, 61).Select(i => (byte)i).ToArray();
        var sealedKey = Enumerable.Range(0, 200).Select(i => (byte)(255 - i)).ToArray();
        var expiresAt = new UnixTimeUtc(1_900_000_000_000);
        await ExecuteAsync(connectionFactory, "UPDATE Circle SET appKeyLockedShareKey=@value WHERE identityId=@identityId;",
            ("@value", lockedKey));
        await ExecuteAsync(connectionFactory, "UPDATE CircleMember SET sealedShareKey=@value WHERE identityId=@identityId;",
            ("@value", sealedKey));
        await ExecuteAsync(connectionFactory, "UPDATE DriveAclIndex SET expiresAt=@value WHERE identityId=@identityId;",
            ("@value", expiresAt.milliseconds));

        Assert.That((await scope.Resolve<TableCircle>().PagingByRowIdAsync(10, null)).Item1[0].appKeyLockedShareKey,
            Is.EqualTo(lockedKey));
        Assert.That((await scope.Resolve<TableCircleMember>().PagingByRowIdAsync(10, null)).Item1[0].sealedShareKey,
            Is.EqualTo(sealedKey));
        Assert.That((await scope.Resolve<TableDriveAclIndex>().PagingByRowIdAsync(10, null)).Item1[0].expiresAt,
            Is.EqualTo(expiresAt));
    }

    private async Task ExecuteAsync(ScopedIdentityConnectionFactory connectionFactory, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cn = await connectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("@identityId", DbType.Binary, IdentityId);
        foreach (var (name, value) in parameters)
        {
            switch (value)
            {
                case Guid g:
                    cmd.AddParameter(name, DbType.Binary, g);
                    break;
                case byte[] b:
                    cmd.AddParameter(name, DbType.Binary, b);
                    break;
                case long l:
                    cmd.AddParameter(name, DbType.Int64, l);
                    break;
                default:
                    throw new ArgumentException($"unsupported parameter {name}");
            }
        }

        Assert.That(await cmd.ExecuteNonQueryAsync(), Is.EqualTo(1), sql);
    }
}
