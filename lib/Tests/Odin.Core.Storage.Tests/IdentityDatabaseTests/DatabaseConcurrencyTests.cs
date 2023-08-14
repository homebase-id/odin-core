﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class DatabaseConcurrencyTests
    {

        [Test]
        public void InsertTest()
        {
            using var db = new IdentityDatabase("");
            db.CreateDatabase();

            var k1 = Guid.NewGuid();
            var k2 = Guid.NewGuid();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r = db.tblKeyValue.Get(k1);
            Debug.Assert(r == null);

            db.tblKeyValue.Insert(new KeyValueRecord() { key = k1, data = v1 });
            db.tblKeyValue.Insert(new KeyValueRecord() { key = k2, data = v2 });

            r = db.tblKeyValue.Get(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();
        }



        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Explicit]
        public void WriteLockingTest()
        {
            List<Guid> Rows = new List<Guid>();

            void writeDB1(IdentityDatabase db)
            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyValue.Update(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
            }

            void writeDB2(IdentityDatabase db)
            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyTwoValue.Insert(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            }
            void readDB(IdentityDatabase db)

            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyValue.Get(Rows[i]);
            }

            using var db = new IdentityDatabase("", 250); // 1ms commit frequency
            db.CreateDatabase();

            for (int i = 0; i < 10000; i++)
            {
                Rows.Add(Guid.NewGuid());
                db.tblKeyValue.Insert(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
            }

            Thread tw1 = new Thread(() => writeDB1(db));
            Thread tw2 = new Thread(() => writeDB2(db));
            Thread tr = new Thread(() => readDB(db));

            tw1.Start();
            tw2.Start();
            tr.Start();

            tw1.Join();
            tw2.Join();
            tr.Join();
        }

        
        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Explicit]
        public void ReaderLockingTest()
        {
            List<Guid> Rows = new List<Guid>();

            void writeDB1(IdentityDatabase db)
            {
                for (int i = 0; i < 10000; i++)
                {
                    db.tblKeyTwoValue.Update(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                    db.Commit();
                }
            }

            void readDB(IdentityDatabase db)

            {
                for (int i = 0; i < 3; i++)
                {
                    var r = db.tblKeyTwoValue.GetByKeyTwo(Guid.Empty.ToByteArray());
                    if (r.Count != 10000)
                        Assert.Fail();
                }
            }

            using var db = new IdentityDatabase("", 250); // 1ms commit frequency
            db.CreateDatabase();

            for (int i = 0; i < 10000; i++)
            {
                Rows.Add(Guid.NewGuid());
                db.tblKeyTwoValue.Insert(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() });
            }

            Thread tr = new Thread(() => readDB(db));
            Thread tw1 = new Thread(() => writeDB1(db));

            tr.Start();
            tw1.Start();

            tw1.Join();
            tr.Join();
        }


        /// <summary>
        /// This test will fail because the two connections cannot both access the database (by design)
        /// </summary>
        [Test, Explicit]
        public void TwoInstanceLockingTest()
        {
            using var db1 = new IdentityDatabase("DataSource=mansi.db");
            db1.CreateDatabase();
            try
            {
                using var db2 = new IdentityDatabase("DataSource=mansi.db");
                Assert.Fail("It's supposed to do a database lock");

            }
            catch (Exception ex)
            {
                Assert.Pass(ex.Message);
            }

        }
    }
}