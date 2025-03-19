using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.Database.Identity.Table
{
    public class TableMainIndexTests : IocTestBase
    {
        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateReactionSummary(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);

            var r = await tblDriveMainIndex.GetAsync(driveId, f1);
            var s = r.hdrReactionSummary;
            var m = r.modified;
            ClassicAssert.IsTrue(m == null);

            var s2 = "a new summary";
            await tblDriveMainIndex.UpdateReactionSummaryAsync(driveId, f1, s2);
            var r2 = await tblDriveMainIndex.GetAsync(driveId, f1);
            var m2 = r2.modified;
            ClassicAssert.IsTrue(r2.hdrReactionSummary == s2);
            ClassicAssert.IsTrue(m2 != null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateTransferStatus(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);

            var r = await tblDriveMainIndex.GetAsync(driveId, f1);
            var s = r.hdrTransferHistory;
            var m = r.modified;
            ClassicAssert.IsTrue(m == null);

            var s2 = "a new transfer status";
            var (count, modified) = await tblDriveMainIndex.UpdateTransferSummaryAsync(driveId, f1, s2);
            var r2 = await tblDriveMainIndex.GetAsync(driveId, f1);
            var m2 = r2.modified;
            ClassicAssert.IsTrue(r2.hdrTransferHistory == s2);
            ClassicAssert.IsTrue(m2 != null);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task GetSizeTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 2);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 3);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 4);
            await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 5);

            var (count, size) = await tblDriveMainIndex.GetDriveSizeDirtyAsync(driveId);
            ClassicAssert.AreEqual(count, 5);
            ClassicAssert.AreEqual(size, 1 + 2 + 3 + 4 + 5);
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task GetSizeInvalidTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var (count, size) = await tblDriveMainIndex.GetDriveSizeDirtyAsync(driveId);
            ClassicAssert.AreEqual(count, 0);
            ClassicAssert.AreEqual(size, 0);
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task CannotInsertZeroSizeTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            try
            {
                await metaIndex.AddEntryPassalongToUpsertAsync(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 0);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException))
                    Assert.Fail();
            }
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            var md = await tblDriveMainIndex.GetAsync(driveId, k1);

            if (md != null)
                Assert.Fail();

            await tblDriveMainIndex.InsertAsync(new DriveMainIndexRecord()
            {
                driveId = driveId,
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
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
            });

            var cts2 = UnixTimeUtc.Now();

            md = await tblDriveMainIndex.GetAsync(driveId, k1);

            if (md == null)
                Assert.Fail();

            ClassicAssert.IsTrue((md.created >= cts1) && (md.created <= cts2));

            if (md.modified != null)
                Assert.Fail();

            if (md.fileType != 7)
                Assert.Fail();

            if (md.dataType != 42)
                Assert.Fail();

            ClassicAssert.True(md.requiredSecurityGroup == 44);

            if (md.senderId.ToString() != sid1.ToString())
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md.groupId, tid1) != 0)
                Assert.Fail();

            if (md.userDate != ud1)
                Assert.Fail();

            if (md.archivalStatus != 0)
                Assert.Fail();

            if (md.historyStatus != 1)
                Assert.Fail();
        }

        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task InsertRowDuplicateTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            await tblDriveMainIndex.InsertAsync(new DriveMainIndexRecord()
            {
                driveId = driveId,
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                fileSystemType = 44,
                hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                hdrVersionTag = SequentialGuid.CreateGuid(),
                hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid()
            });

            try
            {
                await tblDriveMainIndex.InsertAsync(new DriveMainIndexRecord()
                {
                    driveId = driveId,
                    fileId = k1,
                    globalTransitId = Guid.NewGuid(),
                    created = cts1,
                    fileType = 7,
                    dataType = 42,
                    senderId = sid1.ToString(),
                    groupId = tid1,
                    uniqueId = Guid.NewGuid(),
                    userDate = ud1,
                    archivalStatus = 0,
                    historyStatus = 1,
                    fileSystemType = 44,
                    hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                    hdrVersionTag = SequentialGuid.CreateGuid(),
                    hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                    hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                    hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                    hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                    hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                    hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                    hdrTmpDriveType = SequentialGuid.CreateGuid()
                });
                Assert.Fail();
            }
            catch
            {
            }
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpdateRowTest(DatabaseType databaseType)
        {
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            await tblDriveMainIndex.InsertAsync(new DriveMainIndexRecord()
            {
                driveId = driveId,
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                requiredSecurityGroup = 44,
                byteCount = 7,
                hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                hdrVersionTag = SequentialGuid.CreateGuid(),
                hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid(),
                hdrLocalVersionTag = SequentialGuid.CreateGuid(),
                hdrLocalAppData = "localAppData"
            });

            var md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (md.modified != null)
                Assert.Fail();

            md.fileType = 8;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (md.fileType != 8)
                Assert.Fail();

            md.dataType = 43;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (md.dataType != 43)
                Assert.Fail();

            var sid2 = "frodo.baggins";
            md.senderId = sid2;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (sid2 != md.senderId)
                Assert.Fail();

            var tid2 = Guid.NewGuid();
            md.groupId = tid2;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (ByteArrayUtil.muidcmp(tid2, md.groupId) != 0)
                Assert.Fail();

            Guid? uid = Guid.NewGuid();
            md.uniqueId = uid;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (ByteArrayUtil.muidcmp(uid, md.uniqueId) != 0)
                Assert.Fail();

            uid = null;
            md.uniqueId = uid;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (md.uniqueId != null)
                Assert.Fail();

            var gtid2 = Guid.NewGuid();
            md.globalTransitId = gtid2;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (ByteArrayUtil.muidcmp(gtid2, md.globalTransitId) != 0)
                Assert.Fail();


            var ud2 = UnixTimeUtc.Now();
            md.userDate = ud2;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            if (ud2 != md.userDate)
                Assert.Fail();

            md.requiredSecurityGroup = 55;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.True(md.requiredSecurityGroup == 55);

            md.byteCount = 42;
            await tblDriveMainIndex.UpdateAsync(md);
            md = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.True(md.byteCount == 42);

            if (md.modified == 0)
                Assert.Fail();

        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpsertTest(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            // Generate identifiers
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            // Create a new record
            var ndr = new DriveMainIndexRecord()
            {
                driveId = driveId,
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                requiredSecurityGroup = 44,
                byteCount = 7,
                hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                hdrVersionTag = SequentialGuid.CreateGuid(),
                hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid(),
                hdrLocalVersionTag = SequentialGuid.CreateGuid(),
                hdrLocalAppData = "localAppData"
            };

            // Upsert the record
            var n = await tblDriveMainIndex.UpsertAsync(ndr);
            ClassicAssert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            // Retrieve the record and verify
            var md = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.IsNotNull(md, "Retrieved record is null");
            ClassicAssert.IsNull(md.modified, "Retrieved record should not have 'modified' set");

            // Validate all fields match between ndr and md
            ClassicAssert.AreEqual(ndr.driveId, md.driveId, "DriveId mismatch");
            ClassicAssert.AreEqual(ndr.fileId, md.fileId, "FileId mismatch");
            ClassicAssert.AreEqual(ndr.globalTransitId, md.globalTransitId, "GlobalTransitId mismatch");
            ClassicAssert.AreEqual(ndr.created, md.created, "Created timestamp mismatch");
            ClassicAssert.AreEqual(ndr.fileType, md.fileType, "FileType mismatch");
            ClassicAssert.AreEqual(ndr.dataType, md.dataType, "DataType mismatch");
            ClassicAssert.AreEqual(ndr.senderId, md.senderId, "SenderId mismatch");
            ClassicAssert.AreEqual(ndr.groupId, md.groupId, "GroupId mismatch");
            ClassicAssert.AreEqual(ndr.uniqueId, md.uniqueId, "UniqueId mismatch");
            ClassicAssert.AreEqual(ndr.userDate, md.userDate, "UserDate mismatch");
            ClassicAssert.AreEqual(ndr.archivalStatus, md.archivalStatus, "ArchivalStatus mismatch");
            ClassicAssert.AreEqual(ndr.historyStatus, md.historyStatus, "HistoryStatus mismatch");
            ClassicAssert.AreEqual(ndr.requiredSecurityGroup, md.requiredSecurityGroup, "RequiredSecurityGroup mismatch");
            ClassicAssert.AreEqual(ndr.byteCount, md.byteCount, "ByteCount mismatch");
            ClassicAssert.AreEqual(ndr.hdrEncryptedKeyHeader, md.hdrEncryptedKeyHeader, "HdrEncryptedKeyHeader mismatch");
            ClassicAssert.AreEqual(ndr.hdrVersionTag, md.hdrVersionTag, "HdrVersionTag mismatch");
            ClassicAssert.AreEqual(ndr.hdrAppData, md.hdrAppData, "HdrAppData mismatch");
            ClassicAssert.AreEqual(ndr.hdrReactionSummary, md.hdrReactionSummary, "HdrReactionSummary mismatch");
            ClassicAssert.AreEqual(ndr.hdrServerData, md.hdrServerData, "HdrServerData mismatch");
            ClassicAssert.AreEqual(ndr.hdrTransferHistory, md.hdrTransferHistory, "HdrTransferHistory mismatch");
            ClassicAssert.AreEqual(ndr.hdrFileMetaData, md.hdrFileMetaData, "HdrFileMetaData mismatch");
            ClassicAssert.AreEqual(ndr.hdrTmpDriveAlias, md.hdrTmpDriveAlias, "HdrTmpDriveAlias mismatch");
            ClassicAssert.AreEqual(ndr.hdrTmpDriveType, md.hdrTmpDriveType, "HdrTmpDriveType mismatch");
            ClassicAssert.AreEqual(ndr.hdrLocalVersionTag, md.hdrLocalVersionTag, "HdrLocalVersionTag mismatch");
            ClassicAssert.AreEqual(ndr.hdrLocalAppData, md.hdrLocalAppData, "HdrLocalAppData mismatch");

            // Modify all fields except driveId and fileId
            md.fileType = 8;
            md.dataType = 43;
            md.senderId = Guid.NewGuid().ToString();
            md.groupId = Guid.NewGuid();
            md.uniqueId = Guid.NewGuid();
            md.userDate = UnixTimeUtc.Now();
            md.archivalStatus = 1;
            md.historyStatus = 2;
            md.requiredSecurityGroup = 45;
            md.byteCount = 8;
            md.hdrEncryptedKeyHeader = """{"guid12": "abcd4567-e89b-12d3-a456-426614174000"}""";
            md.hdrVersionTag = SequentialGuid.CreateGuid();
            md.hdrAppData = """{"2newAppData": "abcd4567-e89b-12d3-a456-426614174000"}""";
            md.hdrReactionSummary = """{"2reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrServerData = """ {"2serverData": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrTransferHistory = """{"2TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrFileMetaData = """{"2fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrTmpDriveAlias = SequentialGuid.CreateGuid();
            md.hdrTmpDriveType = SequentialGuid.CreateGuid();
            md.hdrLocalVersionTag = SequentialGuid.CreateGuid();
            md.hdrLocalAppData = "2localAppData";

            // Upsert the modified record
            var n2 = await tblDriveMainIndex.UpsertAsync(md);
            ClassicAssert.AreEqual(1, n2, "Upsert failed for modified record");

            // Retrieve the updated record and verify modifications
            var md2 = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.IsNotNull(md2, "Retrieved modified record is null");
            ClassicAssert.IsNotNull(md2.modified, "Modified record should have 'modified' set");

            // Validate all modified fields match
            ClassicAssert.AreEqual(md.fileType, md2.fileType, "FileType mismatch after modification");
            ClassicAssert.AreEqual(md.dataType, md2.dataType, "DataType mismatch after modification");
            ClassicAssert.AreEqual(md.senderId, md2.senderId, "SenderId mismatch after modification");
            ClassicAssert.AreEqual(md.groupId, md2.groupId, "GroupId mismatch after modification");
            ClassicAssert.AreEqual(md.uniqueId, md2.uniqueId, "UniqueId mismatch after modification");
            ClassicAssert.AreEqual(md.userDate, md2.userDate, "UserDate mismatch after modification");
            ClassicAssert.AreEqual(md.archivalStatus, md2.archivalStatus, "ArchivalStatus mismatch after modification");
            ClassicAssert.AreEqual(md.historyStatus, md2.historyStatus, "HistoryStatus mismatch after modification");
            ClassicAssert.AreEqual(md.requiredSecurityGroup, md2.requiredSecurityGroup, "RequiredSecurityGroup mismatch after modification");
            ClassicAssert.AreEqual(md.byteCount, md2.byteCount, "ByteCount mismatch after modification");
            ClassicAssert.AreEqual(md.hdrEncryptedKeyHeader, md2.hdrEncryptedKeyHeader, "HdrEncryptedKeyHeader mismatch after modification");
            ClassicAssert.AreEqual(md.hdrVersionTag, md2.hdrVersionTag, "HdrVersionTag mismatch after modification");
            ClassicAssert.AreEqual(md.hdrAppData, md2.hdrAppData, "HdrAppData mismatch after modification");
            ClassicAssert.AreEqual(md.hdrReactionSummary, md2.hdrReactionSummary, "HdrReactionSummary mismatch after modification");
            ClassicAssert.AreEqual(md.hdrServerData, md2.hdrServerData, "HdrServerData mismatch after modification");
            ClassicAssert.AreEqual(md.hdrTransferHistory, md2.hdrTransferHistory, "HdrTransferHistory mismatch after modification");
            ClassicAssert.AreEqual(md.hdrFileMetaData, md2.hdrFileMetaData, "HdrFileMetaData mismatch after modification");
            ClassicAssert.AreEqual(md.hdrTmpDriveAlias, md2.hdrTmpDriveAlias, "HdrTmpDriveAlias mismatch after modification");
            ClassicAssert.AreEqual(md.hdrTmpDriveType, md2.hdrTmpDriveType, "HdrTmpDriveType mismatch after modification");
            ClassicAssert.AreEqual(md.hdrLocalVersionTag, md2.hdrLocalVersionTag, "HdrLocalVersionTag mismatch after modification");
            ClassicAssert.AreEqual(md.hdrLocalAppData, md2.hdrLocalAppData, "HdrLocalAppData mismatch after modification");
        }


        [Test]
        [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
        [TestCase(DatabaseType.Postgres)]
#endif
        public async Task UpsertAllButReactionsAndTransferAsyncTest(DatabaseType databaseType)
        {
            // Register services and resolve dependencies
            await RegisterServicesAsync(databaseType);
            await using var scope = Services.BeginLifetimeScope();
            var tblDriveMainIndex = scope.Resolve<TableDriveMainIndex>();
            var metaIndex = scope.Resolve<MainIndexMeta>();

            // Generate identifiers
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtc.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            // Create a new record
            var ndr = new DriveMainIndexRecord()
            {
                identityId = this.IdentityId,
                driveId = driveId,
                fileId = k1,
                globalTransitId = Guid.NewGuid(),
                created = cts1,
                fileType = 7,
                dataType = 42,
                senderId = sid1.ToString(),
                groupId = tid1,
                uniqueId = Guid.NewGuid(),
                userDate = ud1,
                archivalStatus = 0,
                historyStatus = 1,
                requiredSecurityGroup = 44,
                byteCount = 7,
                hdrEncryptedKeyHeader = """{"guid1": "123e4567-e89b-12d3-a456-426614174000", "guid2": "987f6543-e21c-45d6-b789-123456789abc"}""",
                hdrVersionTag = SequentialGuid.CreateGuid(),
                hdrAppData = """{"myAppData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrReactionSummary = """{"reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrServerData = """ {"serverData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTransferHistory = """{"TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrFileMetaData = """{"fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""",
                hdrTmpDriveAlias = SequentialGuid.CreateGuid(),
                hdrTmpDriveType = SequentialGuid.CreateGuid(),
                // hdrLocalVersionTag = SequentialGuid.CreateGuid(),
                // hdrLocalAppData = "localAppData"
            };

            // Upsert the record
            var n = await tblDriveMainIndex.UpsertAllButReactionsAndTransferAsync(ndr);
            ClassicAssert.AreEqual(1, n, "Upsert failed: Expected 1 record affected");

            // Retrieve the record and verify
            var md = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.IsNotNull(md, "Retrieved record is null");
            ClassicAssert.IsNull(md.modified, "Retrieved record should not have 'modified' set");

            // Validate all fields match between ndr and md
            ClassicAssert.AreEqual(ndr.driveId, md.driveId, "DriveId mismatch");
            ClassicAssert.AreEqual(ndr.fileId, md.fileId, "FileId mismatch");
            ClassicAssert.AreEqual(ndr.globalTransitId, md.globalTransitId, "GlobalTransitId mismatch");
            ClassicAssert.AreEqual(ndr.created, md.created, "Created timestamp mismatch");
            ClassicAssert.AreEqual(ndr.fileType, md.fileType, "FileType mismatch");
            ClassicAssert.AreEqual(ndr.dataType, md.dataType, "DataType mismatch");
            ClassicAssert.AreEqual(ndr.senderId, md.senderId, "SenderId mismatch");
            ClassicAssert.AreEqual(ndr.groupId, md.groupId, "GroupId mismatch");
            ClassicAssert.AreEqual(ndr.uniqueId, md.uniqueId, "UniqueId mismatch");
            ClassicAssert.AreEqual(ndr.userDate, md.userDate, "UserDate mismatch");
            ClassicAssert.AreEqual(ndr.archivalStatus, md.archivalStatus, "ArchivalStatus mismatch");
            ClassicAssert.AreEqual(ndr.historyStatus, md.historyStatus, "HistoryStatus mismatch");
            ClassicAssert.AreEqual(ndr.requiredSecurityGroup, md.requiredSecurityGroup, "RequiredSecurityGroup mismatch");
            ClassicAssert.AreEqual(ndr.byteCount, md.byteCount, "ByteCount mismatch");
            ClassicAssert.AreEqual(ndr.hdrEncryptedKeyHeader, md.hdrEncryptedKeyHeader, "HdrEncryptedKeyHeader mismatch");
            ClassicAssert.AreEqual(ndr.hdrVersionTag, md.hdrVersionTag, "HdrVersionTag mismatch");
            ClassicAssert.AreEqual(ndr.hdrAppData, md.hdrAppData, "HdrAppData mismatch");
            ClassicAssert.AreEqual(null, md.hdrReactionSummary, "HdrReactionSummary mismatch");
            ClassicAssert.AreEqual(ndr.hdrServerData, md.hdrServerData, "HdrServerData mismatch");
            ClassicAssert.AreEqual(null, md.hdrTransferHistory, "HdrTransferHistory mismatch");
            ClassicAssert.AreEqual(ndr.hdrFileMetaData, md.hdrFileMetaData, "HdrFileMetaData mismatch");
            ClassicAssert.AreEqual(ndr.hdrTmpDriveAlias, md.hdrTmpDriveAlias, "HdrTmpDriveAlias mismatch");
            ClassicAssert.AreEqual(ndr.hdrTmpDriveType, md.hdrTmpDriveType, "HdrTmpDriveType mismatch");
           
            //Note: local version info is not updated with the normal updates for drive main index; there are dedicated methods for this
            // ClassicAssert.AreEqual(ndr.hdrLocalVersionTag, md.hdrLocalVersionTag, "HdrLocalVersionTag mismatch");
            // ClassicAssert.AreEqual(ndr.hdrLocalAppData, md.hdrLocalAppData, "HdrLocalAppData mismatch");

            // Modify all fields except driveId and fileId
            md.fileType = 8;
            md.dataType = 43;
            md.senderId = Guid.NewGuid().ToString();
            md.groupId = Guid.NewGuid();
            md.uniqueId = Guid.NewGuid();
            md.userDate = UnixTimeUtc.Now();
            md.archivalStatus = 1;
            md.historyStatus = 2;
            md.requiredSecurityGroup = 45;
            md.byteCount = 8;
            md.hdrEncryptedKeyHeader = """{"guid12": "abcd4567-e89b-12d3-a456-426614174000"}""";
            md.hdrVersionTag = SequentialGuid.CreateGuid();
            md.hdrAppData = """{"2newAppData": "abcd4567-e89b-12d3-a456-426614174000"}""";
            md.hdrReactionSummary = """{"2reactionSummary": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrServerData = """ {"2serverData": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrTransferHistory = """{"2TransferStatus": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrFileMetaData = """{"2fileMetaData": "123e4567-e89b-12d3-a456-426614174000"}""";
            md.hdrTmpDriveAlias = SequentialGuid.CreateGuid();
            md.hdrTmpDriveType = SequentialGuid.CreateGuid();
            // md.hdrLocalVersionTag = SequentialGuid.CreateGuid();
            // md.hdrLocalAppData = "2localAppData";

            // Upsert the modified record
            var n2 = await tblDriveMainIndex.UpsertAllButReactionsAndTransferAsync(md);
            ClassicAssert.AreEqual(1, n2, "Upsert failed for modified record");

            // Retrieve the updated record and verify modifications
            var md2 = await tblDriveMainIndex.GetAsync(driveId, k1);
            ClassicAssert.IsNotNull(md2, "Retrieved modified record is null");
            ClassicAssert.IsNotNull(md2.modified, "Modified record should have 'modified' set");

            // Validate all modified fields match
            ClassicAssert.AreEqual(md.fileType, md2.fileType, "FileType mismatch after modification");
            ClassicAssert.AreEqual(md.dataType, md2.dataType, "DataType mismatch after modification");
            ClassicAssert.AreEqual(md.senderId, md2.senderId, "SenderId mismatch after modification");
            ClassicAssert.AreEqual(md.groupId, md2.groupId, "GroupId mismatch after modification");
            ClassicAssert.AreEqual(md.uniqueId, md2.uniqueId, "UniqueId mismatch after modification");
            ClassicAssert.AreEqual(md.userDate, md2.userDate, "UserDate mismatch after modification");
            ClassicAssert.AreEqual(md.archivalStatus, md2.archivalStatus, "ArchivalStatus mismatch after modification");
            ClassicAssert.AreEqual(md.historyStatus, md2.historyStatus, "HistoryStatus mismatch after modification");
            ClassicAssert.AreEqual(md.requiredSecurityGroup, md2.requiredSecurityGroup, "RequiredSecurityGroup mismatch after modification");
            ClassicAssert.AreEqual(md.byteCount, md2.byteCount, "ByteCount mismatch after modification");
            ClassicAssert.AreEqual(md.hdrEncryptedKeyHeader, md2.hdrEncryptedKeyHeader, "HdrEncryptedKeyHeader mismatch after modification");
            ClassicAssert.AreEqual(md.hdrVersionTag, md2.hdrVersionTag, "HdrVersionTag mismatch after modification");
            ClassicAssert.AreEqual(md.hdrAppData, md2.hdrAppData, "HdrAppData mismatch after modification");
            ClassicAssert.AreEqual(null, md2.hdrReactionSummary, "HdrReactionSummary mismatch after modification");
            ClassicAssert.AreEqual(md.hdrServerData, md2.hdrServerData, "HdrServerData mismatch after modification");
            ClassicAssert.AreEqual(null, md2.hdrTransferHistory, "HdrTransferHistory mismatch after modification");
            ClassicAssert.AreEqual(md.hdrFileMetaData, md2.hdrFileMetaData, "HdrFileMetaData mismatch after modification");
            ClassicAssert.AreEqual(md.hdrTmpDriveAlias, md2.hdrTmpDriveAlias, "HdrTmpDriveAlias mismatch after modification");
            ClassicAssert.AreEqual(md.hdrTmpDriveType, md2.hdrTmpDriveType, "HdrTmpDriveType mismatch after modification");
            
            //Note: local version info is not updated with the normal updates for drive main index; there are dedicated methods for this
            
            // ClassicAssert.AreEqual(md.hdrLocalVersionTag, md2.hdrLocalVersionTag, "HdrLocalVersionTag mismatch after modification");
            // ClassicAssert.AreEqual(md.hdrLocalAppData, md2.hdrLocalAppData, "HdrLocalAppData mismatch after modification");
        }
    }
}
