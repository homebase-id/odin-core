using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;


namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class DatabaseConnectionTests
    {
        private IdentityDatabase _db;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _db = new IdentityDatabase(Guid.NewGuid(), "massif.db");
            using (var myc = _db.CreateDisposableConnection())
            {
                _db.CreateDatabase();
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _db.Dispose();
        }

        /// <summary>
        /// Ensure that the memory DB doesn't become empty on the second connection
        /// while the first connection is still open
        /// </summary>
        [Test]
        public void RollbackTest()
        {
            using var db1 = new IdentityDatabase(Guid.NewGuid(), "gollum.db");

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            using (var myc1 = db1.CreateDisposableConnection())
            {
                db1.CreateDatabase();
                db1.tblKeyValue.Insert(myc1, new KeyValueRecord() { key = k1, data = v1 });

                using (var myc2 = db1.CreateDisposableConnection())
                {
                    myc2.CreateCommitUnitOfWork(() =>
                    {
                        db1.tblKeyValue.Insert(myc2, new KeyValueRecord() { key = k2, data = v2 });
                        myc2.Dispose(); // Will trigger rollback
                    });
                }

                var r = db1.tblKeyValue.Get(myc1, k1);
                Assert.IsNotNull(r);

                r = db1.tblKeyValue.Get(myc1, k2);
                Assert.IsNull(r);
            }
        }


        /// <summary>
        /// The memory DB will become empty on the second connection
        /// while the first connection is still open
        /// </summary>
        [Test]
        public void MemoryDatabaseDualConnectionTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "gollum2");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

                var k1 = Guid.NewGuid().ToByteArray();
                var k2 = Guid.NewGuid().ToByteArray();
                var v1 = Guid.NewGuid().ToByteArray();
                var v2 = Guid.NewGuid().ToByteArray();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();

                Thread.Sleep(1000);

                using (var myc2 = db.CreateDisposableConnection())
                {
                    r = db.tblKeyValue.Get(myc2, k1);
                    if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                        Assert.Fail();
                }

            }
        }

        [Test]
        [Ignore("No longer relevant, we cannot use memory with new connection design")]
        public void ConnectionDatabaseIncorrectTest()
        {
            using var db1 = new IdentityDatabase(Guid.NewGuid(), ":memory:");
            using var db2 = new IdentityDatabase(Guid.NewGuid(), ":memory:");

            using (var myc1 = db1.CreateDisposableConnection())
            {
                using (var myc2 = db2.CreateDisposableConnection())
                {
                    try
                    {
                        db1.CreateDatabase();
                        Assert.Fail();
                    }
                    catch (ArgumentException)
                    {
                        Assert.Pass();
                    }
                }
            }
        }

        /// <summary>
        /// Ensure that we can reuse prepared statments over two connections
        /// </summary>
        [Test]
        public void DualConnectionPreparedStatementTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), "disco");

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var k3 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();
            var v3 = Guid.NewGuid().ToByteArray();

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

                var r = db.tblKeyValue.Get(myc, k1);
                Debug.Assert(r == null);

                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k2, data = v2 });

                r = db.tblKeyValue.Get(myc, k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }

            using (var myc2 = db.CreateDisposableConnection())
            {
                db.tblKeyValue.Insert(myc2, new KeyValueRecord() { key = k3, data = v3 });

                var r = db.tblKeyValue.Get(myc2, k3);
                if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
                    Assert.Fail();
            }
        }



        [Test]
        public async Task MassivePreparedConnectionTest()
        {
            await PerformanceFramework.ThreadedTestAsync(20, 100, MassiveConnectionPreparedStatementTest);
        }

        private async Task<(long, long[])> MassiveConnectionPreparedStatementTest(int threadNo, int iterations)
        {
            long[] timers = new long[iterations];
            var sw = new Stopwatch();

            using (var myc = _db.CreateDisposableConnection())
            {
                for (int i=0; i < iterations; i++)
                {
                    sw.Restart();
                    var k1 = Guid.NewGuid().ToByteArray();
                    var v1 = Guid.NewGuid().ToByteArray();

                    var r = _db.tblKeyValue.Get(myc, k1);
                    _db.tblKeyValue.Insert(myc, new KeyValueRecord() { key = k1, data = v1 });
                    r = _db.tblKeyValue.Get(myc, k1);
                    timers[i] = sw.ElapsedMilliseconds;
                }
            }

            await Task.Delay(1);

            return (0, timers);
        }
    }
}