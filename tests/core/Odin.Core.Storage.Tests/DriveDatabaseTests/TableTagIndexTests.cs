using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Odin.Core.Storage.Tests.DriveDatabaseTests
{
    
    public class TableTagIndexTests
    {
        [Test]
        // Test we can insert and read a row
        public async Task InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests001");

                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                var md = await db.tblDriveTagIndex.GetAsync(driveId, k1);

                if (md.Count > 1)
                    Assert.Fail();

                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);

                md = await db.tblDriveTagIndex.GetAsync(driveId, k1);

                if (md.Count == 0)
                    Assert.Fail();

                if (md.Count != 1)
                    Assert.Fail();

                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();
        }

        [Test]
        // Test we can insert and read two tagmembers
        public async Task InsertDoubleRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());
                a1.Add(Guid.NewGuid());

                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);

                var md = await db.tblDriveTagIndex.GetAsync(driveId, k1);

                if (md == null)
                    Assert.Fail();

                if (md.Count != 2)
                    Assert.Fail();

                // We don't know what order it comes back in :o) Quick hack.
                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                {
                    if (ByteArrayUtil.muidcmp(md[0], a1[1]) != 0)
                        Assert.Fail();
                    if (ByteArrayUtil.muidcmp(md[1], a1[0]) != 0)
                        Assert.Fail();
                }
                else
                {
                    if (ByteArrayUtil.muidcmp(md[1], a1[1]) != 0)
                        Assert.Fail();
                }
            }
        }

        [Test]
        // Test we cannot insert the same tagmember key twice on the same key
        public async Task InsertDuplicatetagMemberTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());
                a1.Add(a1[0]);

                bool ok = false;
                try
                {
                    await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                    ok = false;
                }
                catch
                {
                    ok = true;
                }

                if (!ok)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we can insert the same tagmember on two different keys
        public async Task InsertDoubletagMemberTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k2, a1);

                var md = await db.tblDriveTagIndex.GetAsync(driveId, k1);
                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();

                md = await db.tblDriveTagIndex.GetAsync(driveId, k2);
                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we cannot insert the same key twice
        public async Task InsertDoubleKeyTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                bool ok = false;
                try
                {
                    await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                    ok = false;
                }
                catch
                {
                    ok = true;
                }

                if (!ok)
                    Assert.Fail();
            }
        }


        [Test]
        public async Task DeleteRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests006");

                await db.CreateDatabaseAsync();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                var v1 = Guid.NewGuid();
                var v2 = Guid.NewGuid();

                a1.Add(v1);
                a1.Add(v2);

                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k1, a1);
                await db.tblDriveTagIndex.InsertRowsAsync(driveId, k2, a1);

                // Delete all tagmembers of the first key entirely
                await db.tblDriveTagIndex.DeleteRowAsync(driveId, k1, a1);

                // Check that k1 is now gone
                var md = await db.tblDriveTagIndex.GetAsync(driveId, k1);
                if (md.Count != 0)
                    Assert.Fail();

                // Remove one of the tagmembers from the list, delete it, and make sure we have the other one
                a1.RemoveAt(0); // Remove v1
                await db.tblDriveTagIndex.DeleteRowAsync(driveId, k2, a1);  // Delete v2

                // Check that we have one left
                md = await db.tblDriveTagIndex.GetAsync(driveId, k2);
                if (md.Count != 1)
                    Assert.Fail();

                if (ByteArrayUtil.muidcmp(md[0].ToByteArray(), v1.ToByteArray()) != 0)
                    Assert.Fail();
        }
    }
}