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
        // await tableDriveMainIndexCached.InsertAsync(item1);
        // await tableDriveMainIndexCached.InsertAsync(item2);

    }

    //

}


