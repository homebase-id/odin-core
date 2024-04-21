using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using NUnit.Framework;
using Odin.Core.Storage.SQLite;
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
        public void MemoryDatabaseDualConnectionTest()
        {
            using var db = new IdentityDatabase(Guid.NewGuid(), ":memory:");

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