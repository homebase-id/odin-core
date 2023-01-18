using System;
using System.Collections.Generic;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Storage.SQLite;

namespace IndexerTests
{

    public class TableAclIndexTests
    {
        [Test]
        // Test we can insert and read a row
        public void InsertRowTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex1.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            var md = db.TblAclIndex.Get(k1);

            if (md != null)
                Assert.Fail();

            db.TblAclIndex.InsertRows(k1, a1);

            md = db.TblAclIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            if (md.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();
        }

        [Test]
        // Test we can insert and read two aclmembers
        public void InsertDoubleRowTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex2.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(Guid.NewGuid());

            db.TblAclIndex.InsertRows(k1, a1);

            var md = db.TblAclIndex.Get(k1);

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

        [Test]
        // Test we cannot insert the same aclmember key twice on the same key
        public void InsertDuplicateAclMemberTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex3.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(a1[0]);

            bool ok = false;
            try
            {
                db.TblAclIndex.InsertRows(k1, a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();
        }

        [Test]
        // Test we can insert the same aclmember on two different keys
        public void InsertDoubleAclMemberTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex4.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblAclIndex.InsertRows(k1, a1);
            db.TblAclIndex.InsertRows(k2, a1);

            var md = db.TblAclIndex.Get(k1);
            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();

            md = db.TblAclIndex.Get(k2);
            if (ByteArrayUtil.muidcmp(md[0], a1[0]) != 0)
                Assert.Fail();
        }

        [Test]
        // Test we cannot insert the same key twice
        public void InsertDoubleKeyTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex5.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblAclIndex.InsertRows(k1, a1);
            bool ok = false;
            try
            {
                db.TblAclIndex.InsertRows(k1, a1);
                ok = false;
            }
            catch
            {
                ok = true;
            }

            if (!ok)
                Assert.Fail();
        }


        [Test]
        public void DeleteRowTest()
        {
            using var db = new DriveIndexDatabase("URI=file:.\\tblaclindex6.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();

            a1.Add(v1);
            a1.Add(v2);

            db.TblAclIndex.InsertRows(k1, a1);
            db.TblAclIndex.InsertRows(k2, a1);
            
            // Delete all aclmembers of the first key entirely
            db.TblAclIndex.DeleteRow(k1, a1);

            // Check that k1 is now gone
            var md = db.TblAclIndex.Get(k1);
            if (md != null)
                Assert.Fail();

            // Remove one of the aclmembers from the list, delete it, and make sure we have the other one
            a1.RemoveAt(0); // Remove v1
            db.TblAclIndex.DeleteRow(k2, a1);  // Delete v2

            // Check that we have one left
            md = db.TblAclIndex.Get(k2);
            if (md.Count != 1)
                Assert.Fail();

            if (ByteArrayUtil.muidcmp(md[0].ToByteArray(), v1.ToByteArray()) != 0)
                Assert.Fail();
        }
    }
}