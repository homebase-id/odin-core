﻿using System;
using System.Collections.Generic;
using NUnit.Framework;
using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Tests.DriveIndexerTests
{
    
    public class TableTagIndexTests
    {
        [Test]
        // Test we can insert and read a row
        public void InsertRowTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex1.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            var md = db.TblTagIndex.Get(k1);

            if (md != null)
                Assert.Fail();

            db.TblTagIndex.InsertRows(k1, a1);

            md = db.TblTagIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            if (md.Count != 1)
                Assert.Fail();

            if (SequentialGuid.muidcmp(md[0].ToByteArray(), a1[0].ToByteArray()) != 0)
                Assert.Fail();
        }

        [Test]
        // Test we can insert and read two tagmembers
        public void InsertDoubleRowTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex2.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(Guid.NewGuid());

            db.TblTagIndex.InsertRows(k1, a1);

            var md = db.TblTagIndex.Get(k1);

            if (md == null)
                Assert.Fail();

            if (md.Count != 2)
                Assert.Fail();

            // We don't know what order it comes back in :o) Quick hack.
            if (SequentialGuid.muidcmp(md[0].ToByteArray(), a1[0].ToByteArray()) != 0)
            {
                if (SequentialGuid.muidcmp(md[0].ToByteArray(), a1[1].ToByteArray()) != 0)
                    Assert.Fail();
                if (SequentialGuid.muidcmp(md[1].ToByteArray(), a1[0].ToByteArray()) != 0)
                    Assert.Fail();
            }
            else
            {
                if (SequentialGuid.muidcmp(md[1].ToByteArray(), a1[1].ToByteArray()) != 0)
                    Assert.Fail();
            }
        }

        [Test]
        // Test we cannot insert the same tagmember key twice on the same key
        public void InsertDuplicatetagMemberTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex3.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());
            a1.Add(a1[0]);

            bool ok = false;
            try
            {
                db.TblTagIndex.InsertRows(k1, a1);
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
        // Test we can insert the same tagmember on two different keys
        public void InsertDoubletagMemberTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex4.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblTagIndex.InsertRows(k1, a1);
            db.TblTagIndex.InsertRows(k2, a1);

            var md = db.TblTagIndex.Get(k1);
            if (SequentialGuid.muidcmp(md[0].ToByteArray(), a1[0].ToByteArray()) != 0)
                Assert.Fail();

            md = db.TblTagIndex.Get(k2);
            if (SequentialGuid.muidcmp(md[0].ToByteArray(), a1[0].ToByteArray()) != 0)
                Assert.Fail();
        }

        [Test]
        // Test we cannot insert the same key twice
        public void InsertDoubleKeyTest()
        {
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex5.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var a1 = new List<Guid>();
            a1.Add(Guid.NewGuid());

            db.TblTagIndex.InsertRows(k1, a1);
            bool ok = false;
            try
            {
                db.TblTagIndex.InsertRows(k1, a1);
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
            var db = new DriveIndexDatabase("URI=file:.\\tblTagIndex6.db", DatabaseIndexKind.Random);
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var a1 = new List<Guid>();
            var v1 = Guid.NewGuid();
            var v2 = Guid.NewGuid();

            a1.Add(v1);
            a1.Add(v2);

            db.TblTagIndex.InsertRows(k1, a1);
            db.TblTagIndex.InsertRows(k2, a1);

            // Delete all tagmembers of the first key entirely
            db.TblTagIndex.DeleteRow(k1, a1);

            // Check that k1 is now gone
            var md = db.TblTagIndex.Get(k1);
            if (md != null)
                Assert.Fail();

            // Remove one of the tagmembers from the list, delete it, and make sure we have the other one
            a1.RemoveAt(0); // Remove v1
            db.TblTagIndex.DeleteRow(k2, a1);  // Delete v2

            // Check that we have one left
            md = db.TblTagIndex.Get(k2);
            if (md.Count != 1)
                Assert.Fail();

            if (SequentialGuid.muidcmp(md[0].ToByteArray(), v1.ToByteArray()) != 0)
                Assert.Fail();
        }
    }
}