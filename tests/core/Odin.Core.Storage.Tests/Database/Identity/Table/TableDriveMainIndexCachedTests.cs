using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableDriveMainIndexCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableDriveMainIndexCached = scope.Resolve<TableDriveMainIndexCached>();

        //
        // Get only
        //

        var driveId = Guid.NewGuid();

        {
            var records = await tableDriveMainIndexCached.GetAllByDriveIdAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(1));
        }

        {
            var records = await tableDriveMainIndexCached.GetAllByDriveIdAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(1));
        }

        {
            var record = await tableDriveMainIndexCached.GetByUniqueIdAsync(driveId, null, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(2));
        }

        {
            var record = await tableDriveMainIndexCached.GetByUniqueIdAsync(driveId, null, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(2));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(2));
        }

        var uniqueId = Guid.NewGuid();

        {
            var record = await tableDriveMainIndexCached.GetByUniqueIdAsync(driveId, uniqueId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(2));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(3));
        }

        {
            var record = await tableDriveMainIndexCached.GetByUniqueIdAsync(driveId, uniqueId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(3));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(3));
        }

        {
            var record = await tableDriveMainIndexCached.GetByGlobalTransitIdAsync(driveId, null, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(3));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(4));
        }

        {
            var record = await tableDriveMainIndexCached.GetByGlobalTransitIdAsync(driveId, null, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(4));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(4));
        }

        var globalTransitId = Guid.NewGuid();

        {
            var record = await tableDriveMainIndexCached.GetByGlobalTransitIdAsync(driveId, globalTransitId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(4));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(5));
        }

        {
            var record = await tableDriveMainIndexCached.GetByGlobalTransitIdAsync(driveId, globalTransitId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(5));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(5));
        }

        var fileId = Guid.NewGuid();

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(5));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(6));
        }

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(6));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(6));
        }

        //
        // Insert and Get
        //

        var item1 = new DriveMainIndexRecord()
        {
            driveId = driveId,
            fileId = fileId,
            uniqueId = uniqueId,
            globalTransitId = globalTransitId,
            fileType = 7,
            dataType = 42,
            archivalStatus = 0,
            historyStatus = 1,
            requiredSecurityGroup = 44,
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

        await tableDriveMainIndexCached.InsertAsync(item1);
        await tableDriveMainIndexCached.InsertAsync(item2);

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(6));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(7));
        }

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(7));
        }

        {
            var records = await tableDriveMainIndexCached.GetAllByDriveIdAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(8));
        }

        {
            var record = await tableDriveMainIndexCached.GetByUniqueIdAsync(driveId, uniqueId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(9));
        }

        {
            var record = await tableDriveMainIndexCached.GetByGlobalTransitIdAsync(driveId, globalTransitId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(10));
        }

        //
        // Delete and Get
        //

        {
            var record = await tableDriveMainIndexCached.GetAsync(item2.driveId, item2.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(11));
        }

        await tableDriveMainIndexCached.DeleteAsync(item1.driveId, item1.fileId);

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(7));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(12));
        }

        {
            var record = await tableDriveMainIndexCached.GetAsync(item2.driveId, item2.fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(8));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(12));
        }

        {
            var records = await tableDriveMainIndexCached.GetAllByDriveIdAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(8));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(13));
        }

        //
        // Updates and Get
        //
        await tableDriveMainIndexCached.InsertAsync(item1);

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(8));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(14));
        }

        await tableDriveMainIndexCached.UpsertAllButReactionsAndTransferAsync(item1, Guid.NewGuid());

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(8));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(15));
        }

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(9));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(15));
        }

        await tableDriveMainIndexCached.UpdateReactionSummaryAsync(item1.driveId, item1.fileId, "foobar");

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(9));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(16));
        }

        await tableDriveMainIndexCached.UpdateTransferSummaryAsync(item1.driveId, item1.fileId, "abnc");

        {
            var record = await tableDriveMainIndexCached.GetAsync(driveId, fileId, TimeSpan.FromSeconds(1));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(9));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(17));
        }

        //
        // GetDriveSize
        //

        {
            var (count, size) = await tableDriveMainIndexCached.GetDriveSizeAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(size, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(9));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(18));
        }

        {
            var (count, size) = await tableDriveMainIndexCached.GetDriveSizeAsync(driveId, TimeSpan.FromSeconds(1));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(size, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(10));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(18));
        }

        {
            var size = await tableDriveMainIndexCached.GetTotalSizeAllDrivesAsync(TimeSpan.FromSeconds(1));
            Assert.That(size, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(10));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(19));
        }

        {
            var size = await tableDriveMainIndexCached.GetTotalSizeAllDrivesAsync(TimeSpan.FromSeconds(1));
            Assert.That(size, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(11));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(19));
        }

        await tableDriveMainIndexCached.DeleteAsync(item1.driveId, item1.fileId);
        await tableDriveMainIndexCached.InsertAsync(item1);

        {
            var size = await tableDriveMainIndexCached.GetTotalSizeAllDrivesAsync(TimeSpan.FromSeconds(1));
            Assert.That(size, Is.EqualTo(0));
            Assert.That(tableDriveMainIndexCached.Hits, Is.EqualTo(11));
            Assert.That(tableDriveMainIndexCached.Misses, Is.EqualTo(20));
        }
    }

    //

}


