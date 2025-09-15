using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

public class MainIndexMetaTests : IocTestBase
{
    [Test]
    public async Task ItShouldQueryModifiedAsync()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
        var mainIndexMeta = scope.Resolve<MainIndexMeta>();

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

        var allIntRange = new IntRange(start: 0, end: 1000);

        await tableDriveMainIndex.InsertAsync(item1);

        {
            var (records, _, _) = await mainIndexMeta.QueryModifiedAsync(item1.driveId, 100, null!, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(0));
        }

        await tableDriveMainIndex.UpdateReactionSummaryAsync(item1.driveId, item1.fileId, "hopla");

        // await Task.Delay(100); // SEB:TODO delete this when QueryModifiedAsync is fixed

        {
            var (records, _, _) = await mainIndexMeta.QueryModifiedAsync(item1.driveId, 100, null!, requiredSecurityGroup: allIntRange);
            Assert.That(records.Count, Is.EqualTo(1));
        }

    }

    //

}


