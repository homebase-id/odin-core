using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
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
        public async Task LocalVersionTagTest(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var tableDriveLocalTagIndex = scope.Resolve<TableDriveLocalTagIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();

            //
            // Setup - Add a target record to drive main index
            //
            var driveMainIndexRecord = new DriveMainIndexRecord
            {
                identityId = this.IdentityId,
                driveId = driveId,
                fileId = fileId,
                hdrLocalVersionTag = Guid.NewGuid(),
                hdrLocalAppData = "we begin here",

                hdrReactionSummary = "",
                hdrServerData = "",
                hdrTransferHistory = "",
                hdrFileMetaData = "",
                created = UnixTimeUtc.Now(),
                modified = UnixTimeUtc.ZeroTime,
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

            var newVersionTag = Guid.NewGuid();
            var localMetadataContent = "this is some content";

            // Check UpdateLocalAppMetadataAsync returns false when the row doesn't exist
            bool exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, Guid.Empty, newVersionTag, localMetadataContent);
            ClassicAssert.IsTrue(exists == false);

            // Setup
            var n = await tblDriveMainIndex.UpsertAllButReactionsAndTransferAsync(driveMainIndexRecord);
            ClassicAssert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");
            var r1 = await tblDriveMainIndex.GetAsync(driveMainIndexRecord.driveId, driveMainIndexRecord.fileId);
            ClassicAssert.IsTrue(r1.hdrLocalAppData == null);
            ClassicAssert.IsTrue(r1.hdrLocalVersionTag == null);

            // Check UpdateLocalAppMetadataAsync returns true when the row exist and the tag was updated (when the value is null)
            exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, Guid.Empty, newVersionTag, localMetadataContent);
            ClassicAssert.IsTrue(exists);

            // Check UpdateLocalAppMetadataAsync returns true when the row exist and the tag was updated (when the value is not null)
            var prevVersionTag = newVersionTag;
            newVersionTag = Guid.NewGuid();
            exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, prevVersionTag, newVersionTag, localMetadataContent);
            ClassicAssert.IsTrue(exists);

            // Check we get a proper exception when the version tag mismatches
            bool ok = false;

            newVersionTag = Guid.NewGuid();

            try
            {
                exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, Guid.NewGuid(), newVersionTag, localMetadataContent);
            }
            catch (OdinClientException ex)
            {
                if (ex.ErrorCode == OdinClientErrorCode.VersionTagMismatch)
                    ok = true;
            }
            ClassicAssert.IsTrue(ok);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task LocalVersionTagInvalidArgumentsTest(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var tableDriveLocalTagIndex = scope.Resolve<TableDriveLocalTagIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();

            //
            // Setup - Add a target record to drive main index
            //
            var driveMainIndexRecord = new DriveMainIndexRecord
            {
                identityId = this.IdentityId,
                driveId = driveId,
                fileId = fileId,
                hdrLocalVersionTag = Guid.NewGuid(),
                hdrLocalAppData = "we begin here",

                hdrReactionSummary = "",
                hdrServerData = "",
                hdrTransferHistory = "",
                hdrFileMetaData = "",
                created = UnixTimeUtc.Now(),
                modified = UnixTimeUtc.ZeroTime,
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

            var newVersionTag = Guid.NewGuid();
            var localMetadataContent = "this is some content";

            // Check newVersionTag cannot be empty
            bool ok = false;
            try
            {
                bool exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, Guid.NewGuid(), Guid.Empty, localMetadataContent);
            }
            catch (OdinSystemException) 
            {
                ok = true;
            }
            ClassicAssert.IsTrue(ok);


            // Check old and newVersionTag cannot be equal
            ok = false;
            try
            {
                var g = Guid.NewGuid();
                bool exists = await tableDriveLocalTagIndex.UpdateLocalAppMetadataAsync(driveId, fileId, g, g, localMetadataContent);
            }
            catch (ArgumentException)
            {
                ok = true;
            }

            ClassicAssert.IsTrue(ok);
        }

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
            var localTagsDataOperations = scope.Resolve<TableDriveLocalTagIndex>();

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();
            var modifiedTime = UnixTimeUtc.ZeroTime;

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
                created = UnixTimeUtc.Now(),
                modified = UnixTimeUtc.ZeroTime,
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
            ClassicAssert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            //
            // Act 
            //
            var localVersionTag1 = SequentialGuid.CreateGuid();
            var localMetadataContent = "this is some content";
            var affectedCount =
                await localTagsDataOperations.UpdateLocalAppMetadataAsync(driveId, fileId, Guid.Empty, localVersionTag1, localMetadataContent);

            // 
            // Assert
            //
            ClassicAssert.IsTrue(affectedCount, "Upsert failed: Expected affected count not true");

            var updatedRecord = await tblDriveMainIndex.GetAsync(driveId, fileId);

            ClassicAssert.AreEqual(updatedRecord.hdrLocalAppData, localMetadataContent, "local app data not updated");
            ClassicAssert.AreEqual(updatedRecord.hdrLocalVersionTag, localVersionTag1, "version tag not updated");
            ClassicAssert.IsTrue(updatedRecord.modified > modifiedTime, "modified time not updated");
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

            var driveId = Guid.NewGuid();
            var fileId = Guid.NewGuid();

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
                created = UnixTimeUtc.Now(),
                modified = UnixTimeUtc.ZeroTime,
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
            ClassicAssert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            //
            // Act 
            //
            List<Guid> tags = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
            await tableDriveLocalTagIndex.UpdateLocalTagsAsync(driveId, fileId, tags);

            // 
            // Assert
            //
            // check the tags
            var newTags = await tableDriveLocalTagIndex.GetAsync(driveId, fileId);
            CollectionAssert.AreEquivalent(newTags, tags, "Local tags not updated");
        }
    }
}