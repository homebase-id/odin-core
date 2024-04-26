using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;

namespace Odin.Core.Storage.Tests.IdentityDatabaseTests

{
    public class DatabaseConnectionTests
    {

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
                db1.CreateDatabase(myc1);
                db1.tblKeyValue.Insert(myc1, new KeyValueRecord() { key = k1, data = v1 });

                using (var myc2 = db1.CreateDisposableConnection())
                {
                    using (myc2.CreateCommitUnitOfWork())
                    {
                        db1.tblKeyValue.Insert(myc2, new KeyValueRecord() { key = k2, data = v2 });
                        myc2.Dispose(); // Will trigger rollback
                    }
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
                db.CreateDatabase(myc);

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
                        db1.CreateDatabase(myc2);
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
                db.CreateDatabase(myc);

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
    }
}