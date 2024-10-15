using System;
using System.Diagnostics;
using System.Security.Cryptography;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    public class TableMainIndexTests
    {
        [Test]
        public void UpdateReactionSummary()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests090");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();

            db.metaIndex.AddEntryPassalongToUpsert(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);

            var r = db.tblDriveMainIndex.Get(driveId, f1);
            var s = r.hdrReactionSummary;
            var m = r.modified;
            Debug.Assert(m == null);

            var s2 = "a new summary";
            db.tblDriveMainIndex.UpdateReactionSummary(driveId, f1, s2);
            var r2 = db.tblDriveMainIndex.Get(driveId, f1);
            var m2 = r2.modified;
            Debug.Assert(r2.hdrReactionSummary == s2);
            Debug.Assert(m2 != null);
        }

        [Test]
        public void UpdateTransferStatus()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests091");

                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
                var s1 = SequentialGuid.CreateGuid().ToString();
                var t1 = SequentialGuid.CreateGuid();

                db.metaIndex.AddEntryPassalongToUpsert(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);

                var r = db.tblDriveMainIndex.Get(driveId, f1);
                var s = r.hdrTransferHistory;
                var m = r.modified;
                Debug.Assert(m == null);

                var s2 = "a new transfer status";
                db.tblDriveMainIndex.UpdateTransferHistory(driveId, f1, s2);
                var r2 = db.tblDriveMainIndex.Get(driveId, f1);
                var m2 = r2.modified;
                Debug.Assert(r2.hdrTransferHistory == s2);
                Debug.Assert(m2 != null);
        }

        [Test]
        public void GetSizeTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests001");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var f1 = SequentialGuid.CreateGuid(); // Oldest chat item
            var s1 = SequentialGuid.CreateGuid().ToString();
            var t1 = SequentialGuid.CreateGuid();
            var f2 = SequentialGuid.CreateGuid();
            var f3 = SequentialGuid.CreateGuid();
            var f4 = SequentialGuid.CreateGuid();
            var f5 = SequentialGuid.CreateGuid(); // Most recent chat item

            db.metaIndex.AddEntryPassalongToUpsert(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 1);
            db.metaIndex.AddEntryPassalongToUpsert(driveId, f3, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 2);
            db.metaIndex.AddEntryPassalongToUpsert(driveId, f2, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 1, null, null, 3);
            db.metaIndex.AddEntryPassalongToUpsert(driveId, f5, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 3, null, null, 4);
            db.metaIndex.AddEntryPassalongToUpsert(driveId, f4, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 2, null, null, 5);

            var (count, size) = db.tblDriveMainIndex.GetDriveSizeDirty(driveId);
            Assert.AreEqual(count, 5);
            Assert.AreEqual(size, 1 + 2 + 3 + 4 + 5);
        }

        [Test]
        public void GetSizeInvalidTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests002");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var (count, size) = db.tblDriveMainIndex.GetDriveSizeDirty(driveId);
            Assert.AreEqual(count, 0);
            Assert.AreEqual(size, 0);
        }


        [Test]
        public void CannotInsertZeroSizeTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests003");

            db.CreateDatabase();
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
                db.metaIndex.AddEntryPassalongToUpsert(driveId, f1, Guid.NewGuid(), 1, 1, s1, t1, null, 42, new UnixTimeUtc(0), 0, null, null, 0);
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException))
                    Assert.Fail();
            }
        }


        [Test]
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests004");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtcUnique.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            var md = db.tblDriveMainIndex.Get(driveId, k1);

            if (md != null)
                Assert.Fail();

            db.tblDriveMainIndex.Insert(new DriveMainIndexRecord()
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

            var cts2 = UnixTimeUtcUnique.Now();

            md = db.tblDriveMainIndex.Get(driveId, k1);

            if (md == null)
                Assert.Fail();

            Assert.IsTrue((md.created.ToUnixTimeUtc() >= cts1.ToUnixTimeUtc()) && (md.created.ToUnixTimeUtc() <= cts2.ToUnixTimeUtc()));

            if (md.modified != null)
                Assert.Fail();

            if (md.fileType != 7)
                Assert.Fail();

            if (md.dataType != 42)
                Assert.Fail();

            Assert.True(md.requiredSecurityGroup == 44);

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
        public void InsertRowDuplicateTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests005");

            db.CreateDatabase();
            var driveId = Guid.NewGuid();

            var k1 = Guid.NewGuid();
            var cts1 = UnixTimeUtcUnique.Now();
            var sid1 = Guid.NewGuid().ToByteArray();
            var tid1 = Guid.NewGuid();
            var ud1 = UnixTimeUtc.Now();

            db.tblDriveMainIndex.Insert(new DriveMainIndexRecord()
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
                db.tblDriveMainIndex.Insert(new DriveMainIndexRecord()
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
        public void UpdateRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableMainIndexTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var cts1 = UnixTimeUtcUnique.Now();
                var sid1 = Guid.NewGuid().ToByteArray();
                var tid1 = Guid.NewGuid();
                var ud1 = UnixTimeUtc.Now();

                db.tblDriveMainIndex.Insert(new DriveMainIndexRecord()
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
                    hdrTmpDriveType = SequentialGuid.CreateGuid()
                });

                var md = db.tblDriveMainIndex.Get(driveId, k1);
                if (md.modified != null)
                    Assert.Fail();

                md.fileType = 8;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (md.fileType != 8)
                    Assert.Fail();

                md.dataType = 43;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (md.dataType != 43)
                    Assert.Fail();

                var sid2 = "frodo.baggins";
                md.senderId = sid2;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (sid2 != md.senderId)
                    Assert.Fail();

                var tid2 = Guid.NewGuid();
                md.groupId = tid2;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (ByteArrayUtil.muidcmp(tid2, md.groupId) != 0)
                    Assert.Fail();

                Guid? uid = Guid.NewGuid();
                md.uniqueId = uid;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (ByteArrayUtil.muidcmp(uid, md.uniqueId) != 0)
                    Assert.Fail();

                uid = null;
                md.uniqueId = uid;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (md.uniqueId != null)
                    Assert.Fail();

                var gtid2 = Guid.NewGuid();
                md.globalTransitId = gtid2;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (ByteArrayUtil.muidcmp(gtid2, md.globalTransitId) != 0)
                    Assert.Fail();


                var ud2 = UnixTimeUtc.Now();
                md.userDate = ud2;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                if (ud2 != md.userDate)
                    Assert.Fail();

                md.requiredSecurityGroup = 55;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                Assert.True(md.requiredSecurityGroup == 55);

                md.byteCount = 42;
                db.tblDriveMainIndex.Update(myc, md);
                md = db.tblDriveMainIndex.Get(driveId, k1);
                Assert.True(md.byteCount == 42);

                if (md.modified?.uniqueTime == 0)
                    Assert.Fail();
            }
        }
    }
}