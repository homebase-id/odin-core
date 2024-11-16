# if false
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class DatabaseConcurrencyTests
    {
        [Test]
        public async Task InsertTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "DatabaseConcurrencyTests001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();
                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = await db.tblKeyValue.GetAsync(k1);
                Debug.Assert(r == null);

                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
                await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

                r = await db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }
        }


        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Explicit]
        public async Task WriteLockingTest()
        {
            List<byte[]> Rows = new List<byte[]>();

            void writeDB1(IdentityDatabase db, DatabaseConnection myc)
            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyValue.UpdateAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() }).Wait();
            }

            void writeDB2(IdentityDatabase db, DatabaseConnection myc)
            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord()
                    { key1 = Rows[i], key2 = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() }).Wait();
            }

            void readDB(IdentityDatabase db, DatabaseConnection myc)

            {
                for (int i = 0; i < 10000; i++)
                    db.tblKeyValue.GetAsync(Rows[i]).Wait();
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), ""); // 1ms commit frequency

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

                for (int i = 0; i < 10000; i++)
                {
                    Rows.Add(Guid.NewGuid().ToByteArray());
                    await db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = Rows[i], data = Guid.NewGuid().ToByteArray() });
                }

                Thread tw1 = new Thread(() => writeDB1(db, myc));
                Thread tw2 = new Thread(() => writeDB2(db, myc));
                Thread tr = new Thread(() => readDB(db, myc));

                tw1.Start();
                tw2.Start();
                tr.Start();

                tw1.Join();
                tw2.Join();
                tr.Join();
            }
        }

        /// <summary>
        /// This test passes because the Database class locks on it's _transactionLock() object
        /// </summary>
        [Test, Explicit]
        public async Task ReaderLockingTest()
        {
            // List<Guid> Rows = new List<Guid>();
            List<byte[]> Rows = new List<byte[]>();

            void writeDB1(IdentityDatabase db, DatabaseConnection myc)
            {
                for (int i = 0; i < 10000; i++)
                {
                    db.tblKeyTwoValue.UpdateAsync(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() }).Wait();
                }
            }

            void readDB(IdentityDatabase db, DatabaseConnection myc)
            {
                for (int i = 0; i < 3; i++)
                {
                    var r = db.tblKeyTwoValue.GetByKeyTwoAsync(Guid.Empty.ToByteArray()).Result;
                    if (r.Count != 10000)
                        Assert.Fail();
                }
            }

            using var db = new IdentityDatabase(Guid.NewGuid(), ""); // 1ms commit frequency

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

                for (int i = 0; i < 10000; i++)
                {
                    Rows.Add(Guid.NewGuid().ToByteArray());
                    await db.tblKeyTwoValue.InsertAsync(new KeyTwoValueRecord() { key1 = Rows[i], key2 = Guid.Empty.ToByteArray(), data = Guid.NewGuid().ToByteArray() });
                }

                Thread tr = new Thread(() => readDB(db, myc));
                Thread tw1 = new Thread(() => writeDB1(db, myc));

                tr.Start();
                tw1.Start();

                tw1.Join();
                tr.Join();
            }
        }


        /// <summary>
        /// This test will fail because the two connections cannot both access the database (by design)
        /// </summary>
        [Test, Explicit]
        public async Task TwoInstanceLockingTest()
        {
            using var db1 = new IdentityDatabase(Guid.NewGuid(), "DataSource=mansi.db");

            using (var myc = db1.CreateDisposableConnection())
            {
                await db1.CreateDatabaseAsync();
                try
                {
                    using var db2 = new IdentityDatabase(Guid.NewGuid(), "DataSource=mansi.db");
                    Assert.Fail("It's supposed to do a database lock");
                }
                catch (Exception ex)
                {
                    Assert.Pass(ex.Message);
                }
            }
        }
    }
}
#endif