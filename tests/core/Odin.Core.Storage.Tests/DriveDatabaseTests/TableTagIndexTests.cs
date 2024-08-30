using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests
{
    
    public class TableTagIndexTests
    {
        [Test]
        // Test we can insert and read a row
        public void InsertRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                var md = db.tblDriveTagIndex.Get(driveId, k1);

                if (md != null)
                    Assert.Fail();

                db.tblDriveTagIndex.InsertRows(driveId, k1, a1);

                md = db.tblDriveTagIndex.Get(driveId, k1);

                if (md == null)
                    Assert.Fail();

                if (md.Count != 1)
                    Assert.Fail();

                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDoubleRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests002");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());
                a1.Add(Guid.NewGuid());

                db.tblDriveTagIndex.InsertRows(driveId, k1, a1);

                var md = db.tblDriveTagIndex.Get(driveId, k1);

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
        public void InsertDuplicatetagMemberTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests003");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());
                a1.Add(a1[0]);

                bool ok = false;
                try
                {
                    db.tblDriveTagIndex.InsertRows(driveId, k1, a1);
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
        public void InsertDoubletagMemberTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests004");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                db.tblDriveTagIndex.InsertRows(driveId, k1, a1);
                db.tblDriveTagIndex.InsertRows(driveId, k2, a1);

                var md = db.tblDriveTagIndex.Get(driveId, k1);
                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();

                md = db.tblDriveTagIndex.Get(driveId, k2);
                if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we cannot insert the same key twice
        public void InsertDoubleKeyTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests005");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var a1 = new List<Guid>();
                a1.Add(Guid.NewGuid());

                db.tblDriveTagIndex.InsertRows(driveId, k1, a1);
                bool ok = false;
                try
                {
                    db.tblDriveTagIndex.InsertRows(driveId, k1, a1);
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
        public void DeleteRowTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "TableTagIndexTests006");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();
                var driveId = Guid.NewGuid();

                var k1 = Guid.NewGuid();
                var k2 = Guid.NewGuid();
                var a1 = new List<Guid>();
                var v1 = Guid.NewGuid();
                var v2 = Guid.NewGuid();

                a1.Add(v1);
                a1.Add(v2);

                db.tblDriveTagIndex.InsertRows(driveId, k1, a1);
                db.tblDriveTagIndex.InsertRows(driveId, k2, a1);

                // Delete all tagmembers of the first key entirely
                db.tblDriveTagIndex.DeleteRow(driveId, k1, a1);

                // Check that k1 is now gone
                var md = db.tblDriveTagIndex.Get(driveId, k1);
                if (md != null)
                    Assert.Fail();

                // Remove one of the tagmembers from the list, delete it, and make sure we have the other one
                a1.RemoveAt(0); // Remove v1
                db.tblDriveTagIndex.DeleteRow(driveId, k2, a1);  // Delete v2

                // Check that we have one left
                md = db.tblDriveTagIndex.Get(driveId, k2);
                if (md.Count != 1)
                    Assert.Fail();

                if (ByteArrayUtil.muidcmp(md[0].ToByteArray(), v1.ToByteArray()) != 0)
                    Assert.Fail();
            }
        }
    }
}