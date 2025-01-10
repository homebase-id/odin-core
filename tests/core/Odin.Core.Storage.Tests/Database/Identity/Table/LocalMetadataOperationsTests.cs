using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class LocalMetadataDataOperationsTest : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateLocalMetadataContentOnly(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var localTagsDataOperations = scope.Resolve<LocalMetadataDataOperations>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var modifiedTime = UnixTimeUtcUnique.ZeroTime;

            //
            // Setup - Add a target record to drive main index
            //
            var driveMainIndexRecord = new DriveMainIndexRecord
            {
                identityId = this.IdentityId,
                driveId = driveId,
                fileId = fileId,
                hdrLocalVersionTag = null,
                hdrLocalAppData = null,

                hdrReactionSummary = "",
                hdrServerData = "",
                hdrTransferHistory = "",
                hdrFileMetaData = "",
                created = UnixTimeUtcUnique.Now(),
                modified = UnixTimeUtcUnique.ZeroTime,
                fileSystemType = 1,
                userDate = default,
                fileType = 0,
                dataType = 0,
                archivalStatus = 0,
                historyStatus = 0,
                senderId = "",
                groupId = Guid.NewGuid(),
                globalTransitId = Guid.NewGuid(),
                fileState = 0,
                requiredSecurityGroup = 0,
                uniqueId = Guid.NewGuid(),
                byteCount = 0,
                hdrEncryptedKeyHeader = "some encrypted key header",
                hdrVersionTag = Guid.NewGuid(),
                hdrAppData = "",
                hdrTmpDriveAlias = Guid.NewGuid(),
                hdrTmpDriveType = Guid.NewGuid(),
            };

            var n = await tblDriveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord);
            Assert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            //
            // Act 
            //
            var localVersionTag1 = SequentialGuid.CreateGuid();
            var localMetadataContent = "this is some content";
            var affectedCount =
                await localTagsDataOperations.UpdateLocalAppMetadataContentAsync(driveId, fileId, localVersionTag1, localMetadataContent);

            // 
            // Assert
            //
            Assert.IsTrue(affectedCount == 1, "Upsert failed: Expected affected count of 1");

            var updatedRecord = await tblDriveMainIndex.GetAsync(driveId, fileId);

            Assert.AreEqual(updatedRecord.hdrLocalAppData, localMetadataContent, "local app data not updated");
            Assert.AreEqual(updatedRecord.hdrLocalVersionTag, localVersionTag1, "version tag not updated");
            Assert.IsTrue(updatedRecord.modified.GetValueOrDefault().ToUnixTimeUtc() > modifiedTime.ToUnixTimeUtc(),
                "modified time not updated");
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateLocalMetadataTagsOnly(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var tableDriveLocalTagIndex = scope.Resolve<TableDriveLocalTagIndex>();
            var localTagsDataOperations = scope.Resolve<LocalMetadataDataOperations>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var modifiedTime = UnixTimeUtcUnique.ZeroTime;

            //
            // Setup - Add a target record to drive main index
            //
            var driveMainIndexRecord = new DriveMainIndexRecord
            {
                identityId = this.IdentityId,
                driveId = driveId,
                fileId = fileId,
                hdrLocalVersionTag = null,
                hdrLocalAppData = null,
                hdrReactionSummary = "",
                hdrServerData = "",
                hdrTransferHistory = "",
                hdrFileMetaData = "",
                created = UnixTimeUtcUnique.Now(),
                modified = UnixTimeUtcUnique.ZeroTime,
                fileSystemType = 1,
                userDate = default,
                fileType = 0,
                dataType = 0,
                archivalStatus = 0,
                historyStatus = 0,
                senderId = "",
                groupId = Guid.NewGuid(),
                globalTransitId = Guid.NewGuid(),
                fileState = 0,
                requiredSecurityGroup = 0,
                uniqueId = Guid.NewGuid(),
                byteCount = 0,
                hdrEncryptedKeyHeader = "some encrypted key header",
                hdrVersionTag = Guid.NewGuid(),
                hdrAppData = "",
                hdrTmpDriveAlias = Guid.NewGuid(),
                hdrTmpDriveType = Guid.NewGuid(),
            };

            var n = await tblDriveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord);
            Assert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            //
            // Act 
            //
            var localVersionTag1 = SequentialGuid.CreateGuid();
            List<Guid> tags = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            await localTagsDataOperations.UpdateLocalTagsAsync(driveId, fileId, localVersionTag1, tags);

            // 
            // Assert
            //
            var updatedRecord = await tblDriveMainIndex.GetAsync(driveId, fileId);

            Assert.IsNull(updatedRecord.hdrLocalAppData, "local app metadata should not have been updated");
            Assert.AreEqual(updatedRecord.hdrLocalVersionTag, localVersionTag1, "version tag not updated");
            Assert.IsTrue(updatedRecord.modified.GetValueOrDefault().ToUnixTimeUtc() > modifiedTime.ToUnixTimeUtc(),
                "modified time not updated");

            // check the tags
            var newTags = await tableDriveLocalTagIndex.GetAsync(driveId, fileId);
            CollectionAssert.AreEquivalent(newTags, tags, "Local tags not updated");
        }
    }
}