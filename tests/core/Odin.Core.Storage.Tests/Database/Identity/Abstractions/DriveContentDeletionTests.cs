using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

/// <summary>
/// A hard-deleted file leaves no reaction, local-tag or transfer-history rows, and emptying a drive touches
/// no other drive (#1869).
/// </summary>
public class DriveContentDeletionTests : IocTestBase
{
    private static readonly OdinId Reactor = (OdinId)"sam.dotyou.cloud";

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task HardDeletingAFileRemovesItsReactionsLocalTagsAndTransferHistory(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var meta = scope.Resolve<MainIndexMeta>();
        var driveId = Guid.NewGuid();
        var (deleted, kept) = (Guid.NewGuid(), Guid.NewGuid());
        await AddFileWithEverythingAsync(scope, driveId, deleted);
        await AddFileWithEverythingAsync(scope, driveId, kept);

        await meta.DeleteEntryAsync(driveId, deleted);

        Assert.That(await RowsForFileAsync(scope, driveId, deleted), Is.EqualTo((false, 0, 0, 0)));
        Assert.That(await RowsForFileAsync(scope, driveId, kept), Is.EqualTo((true, 1, 1, 1)));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task EmptyingADriveLeavesOtherDrivesAlone(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var meta = scope.Resolve<MainIndexMeta>();
        var (emptied, other) = (Guid.NewGuid(), Guid.NewGuid());
        var (f1, f2, g1) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await AddFileWithEverythingAsync(scope, emptied, f1);
        await AddFileWithEverythingAsync(scope, emptied, f2);
        await AddFileWithEverythingAsync(scope, other, g1);

        await meta.DeleteDriveContentAsync(emptied);

        Assert.That(await scope.Resolve<TableDriveMainIndex>().GetAllByDriveIdAsync(emptied), Is.Empty);
        Assert.That(await RowsForFileAsync(scope, emptied, f1), Is.EqualTo((false, 0, 0, 0)));
        Assert.That(await RowsForFileAsync(scope, emptied, f2), Is.EqualTo((false, 0, 0, 0)));
        Assert.That(await RowsForFileAsync(scope, other, g1), Is.EqualTo((true, 1, 1, 1)));
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task APurgeBatchDeletesOnlyItsFilesAndHonoursTheCutoff(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var meta = scope.Resolve<MainIndexMeta>();
        var driveId = Guid.NewGuid();
        var (old1, old2) = (Guid.NewGuid(), Guid.NewGuid());
        await AddFileWithEverythingAsync(scope, driveId, old1);
        await AddFileWithEverythingAsync(scope, driveId, old2);
        var cutoff = UnixTimeUtc.Now().milliseconds;
        await Task.Delay(5);
        var newer = Guid.NewGuid();
        await AddFileWithEverythingAsync(scope, driveId, newer);

        Assert.That(await meta.GetDriveFileIdsAsync(driveId, 10, cutoff), Is.EquivalentTo(new[] { old1, old2 }));
        Assert.That(await meta.GetDriveFileIdsAsync(driveId, 1, null), Has.Count.EqualTo(1));

        await meta.DeleteFilesAsync(driveId, [old1]);

        Assert.That(await RowsForFileAsync(scope, driveId, old1), Is.EqualTo((false, 0, 0, 0)));
        Assert.That(await RowsForFileAsync(scope, driveId, old2), Is.EqualTo((true, 1, 1, 1)));
        Assert.That(await RowsForFileAsync(scope, driveId, newer), Is.EqualTo((true, 1, 1, 1)));
    }

    private static async Task AddFileWithEverythingAsync(ILifetimeScope scope, Guid driveId, Guid fileId)
    {
        await scope.Resolve<MainIndexMeta>().TestAddEntryPassalongToUpsertAsync(driveId, fileId, Guid.NewGuid(), 1, 1,
            "frodo.dotyou.cloud", null, null, 0, UnixTimeUtc.Now(), 0, null, null, 1);
        await scope.Resolve<TableDriveLocalTagIndex>().InsertRowsAsync(driveId, fileId, [Guid.NewGuid()]);
        await scope.Resolve<TableDriveReactions>().InsertAsync(new DriveReactionsRecord
        {
            driveId = driveId, postId = fileId, identity = Reactor, singleReaction = ":smile:"
        });
        await scope.Resolve<TableDriveTransferHistory>().TryAddInitialRecordAsync(driveId, fileId, Reactor);
    }

    private static async Task<(bool header, int localTags, int reactions, int transfers)> RowsForFileAsync(
        ILifetimeScope scope, Guid driveId, Guid fileId)
    {
        var header = await scope.Resolve<TableDriveMainIndex>().GetAsync(driveId, fileId);
        var tags = await scope.Resolve<TableDriveLocalTagIndex>().GetAsync(driveId, fileId);
        var (_, reactions) = await scope.Resolve<TableDriveReactions>().GetPostReactionsAsync(driveId, fileId);
        var transfers = await scope.Resolve<TableDriveTransferHistory>().GetAsync(driveId, fileId);
        return (header != null, tags.Count, reactions, transfers.Count);
    }
}
