using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

public class MainIndexMetaCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableDriveMainIndexCached = scope.Resolve<TableDriveMainIndexCached>();
        var mainIndexMetaCached = scope.Resolve<MainIndexMetaCached>();

        //
        // Get only
        //

        var item1 = new DriveMainIndexRecord()
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            //uniqueId = Guid.NewGuid(),
            //globalTransitId = Guid.NewGuid(),
            // fileType = 7,
            // dataType = 42,
            // archivalStatus = 0,
            // historyStatus = 1,
            requiredSecurityGroup = 44,
            fileSystemType = (int)FileSystemType.Standard,
            hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
            hdrVersionTag = SequentialGuid.CreateGuid(),
            hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
            // hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
            // hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
            hdrTmpDriveType = SequentialGuid.CreateGuid()
        };

        var item2 = new DriveMainIndexRecord()
        {
            driveId = Guid.NewGuid(),
            fileId = Guid.NewGuid(),
            uniqueId = Guid.NewGuid(),
            globalTransitId = Guid.NewGuid(),
            fileType = 7,
            dataType = 42,
            archivalStatus = 0,
            historyStatus = 1,
            requiredSecurityGroup = 44,
            fileSystemType = (int)FileSystemType.Standard,
            hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
            hdrVersionTag = SequentialGuid.CreateGuid(),
            hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
            hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
            hdrTmpDriveType = SequentialGuid.CreateGuid()
        };

        var allIntRange = new IntRange(start: 0, end: 1000);
        var cursor = new QueryBatchCursor(UnixTimeUtc.Now());

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(1));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(1));
        }

        //
        // Insert and Get
        //
        await tableDriveMainIndexCached.InsertAsync(item1);

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(2));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(2));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(2));
        }

        await tableDriveMainIndexCached.InsertAsync(item2);

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(3));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(2));
        }

        await tableDriveMainIndexCached.DeleteAsync(item1.driveId, item1.fileId);

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(3));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(3));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(4));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(3));
        }

        await tableDriveMainIndexCached.InsertAsync(item1);

        {
            var record = await tableDriveMainIndexCached.GetAsync(item1.driveId, item1.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(1));
        }

        await mainIndexMetaCached.BaseUpsertEntryZapZapAsync(item1);

        {
            var record = await tableDriveMainIndexCached.GetAsync(item1.driveId, item1.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(2));
        }

        {
            var record = await tableDriveMainIndexCached.GetAsync(item1.driveId, item1.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(2));
        }

        await mainIndexMetaCached.DeleteEntryAsync(item1.driveId, item1.fileId);

        {
            var record = await tableDriveMainIndexCached.GetAsync(item1.driveId, item1.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(3));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(4));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(4));
        }

        await tableDriveMainIndexCached.InsertAsync(item1);

        await mainIndexMetaCached.UpdateLocalTagsAsync(item1.driveId, item1.fileId, [Guid.NewGuid(), Guid.NewGuid()]);

        {
            var record = await tableDriveMainIndexCached.GetAsync(item1.driveId, item1.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(4));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(4));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(5));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchSmartCursorAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(4));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(6));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchSmartCursorAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(5));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(6));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryModifiedAsync(item1.driveId, 100, "cursor", requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(5));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(7));
        }

        {
            var (records, _, _) = await mainIndexMetaCached.QueryModifiedAsync(item1.driveId, 100, "cursor", requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(6));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(7));
        }

        await tableDriveMainIndexCached.UpdateReactionSummaryAsync(item1.driveId, item1.fileId, "hopla");

        {
            var (records, _, _) = await mainIndexMetaCached.QueryBatchSmartCursorAsync(item1.driveId, 100, cursor, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(6));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(8));
        }

        // SEB:NOTE QueryModifiedAsync has issues with time resolution of selecting modified records too close to
        // an update. So we add a small delay here to make sure the record is picked up. We should fix this properly.
        await Task.Delay(100);

        {
            var (records, _, _) = await mainIndexMetaCached.QueryModifiedAsync(item1.driveId, 100, "cursor", requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(mainIndexMetaCached.Hits, Is.EqualTo(6));
            Assert.That(mainIndexMetaCached.Misses, Is.EqualTo(9));
        }

    }

    //

}


