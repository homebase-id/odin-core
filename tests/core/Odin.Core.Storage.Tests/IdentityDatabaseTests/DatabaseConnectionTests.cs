using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.SQLite.IdentityDatabase;


namespace Odin.Core.Storage.Tests.IdentityDatabaseTests;

public class DatabaseConnectionTests
{
    private IdentityDatabase _db;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _db = new IdentityDatabase(Guid.NewGuid(), Guid.NewGuid()+ToString()+".db");
        using (var myc = _db.CreateDisposableConnection())
        {
            await _db.CreateDatabaseAsync();
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _db.Dispose();
    }

    [Test]
    public async Task RollbackTest()
    {
        using var db1 = new IdentityDatabase(Guid.NewGuid(), "gollum.db");

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        using (var myc1 = db1.CreateDisposableConnection())
        {
            await db1.CreateDatabaseAsync();
            await db1.tblKeyValue.InsertAsync(myc1, new KeyValueRecord() { identityId = db1._identityId, key = k1, data = v1 });

            Assert.ThrowsAsync<RollbackException>(async () =>
            {
                using var myc2 = db1.CreateDisposableConnection();
                await myc2.CreateCommitUnitOfWorkAsync(async () =>
                {
                    await db1.tblKeyValue.InsertAsync(myc2,
                        new KeyValueRecord() { identityId = db1._identityId, key = k2, data = v2 });
                    throw new RollbackException("rollback triggered");
                });
            });

            var r = await db1.tblKeyValue.GetAsync(myc1, db1._identityId, k1);
            Assert.IsNotNull(r);

            r = await db1.tblKeyValue.GetAsync(myc1, db1._identityId, k2);
            Assert.IsNull(r);
        }
    }


    /// <summary>
    /// The memory DB will become empty on the second connection
    /// while the first connection is still open
    /// </summary>
    [Test]
    public async Task MemoryDatabaseDualConnectionTest()
    {
        using var db = new IdentityDatabase(Guid.NewGuid(), "gollum2");

        using (var myc = db.CreateDisposableConnection())
        {
            await db.CreateDatabaseAsync();

            var k1 = Guid.NewGuid().ToByteArray();
            var k2 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();
            var v2 = Guid.NewGuid().ToByteArray();

            var r =await  db.tblKeyValue.GetAsync(k1);
            Debug.Assert(r == null);

           await  db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
           await  db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

            r =await  db.tblKeyValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();

            Thread.Sleep(1000);

            using (var myc2 = db.CreateDisposableConnection())
            {
                r =await  db.tblKeyValue.GetAsync(k1);
                if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                    Assert.Fail();
            }

        }
    }


    /// <summary>
    /// Ensure that we can reuse prepared statments over two connections
    /// </summary>
    [Test]
    public async Task DualConnectionPreparedStatementTest()
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
            await db.CreateDatabaseAsync();

            var r =await  db.tblKeyValue.GetAsync(k1);
            Debug.Assert(r == null);

           await  db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
           await  db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

            r =await  db.tblKeyValue.GetAsync(k1);
            if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
                Assert.Fail();
        }

        using (var myc2 = db.CreateDisposableConnection())
        {
           await  db.tblKeyValue.InsertAsync(new KeyValueRecord() { key = k3, data = v3 });

            var r =await  db.tblKeyValue.GetAsync(k3);
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

                var r = await _db.tblKeyValue.GetAsync(k1);
                await _db.tblKeyValue.InsertAsync(myc, new KeyValueRecord() { identityId = _db._identityId, key = k1, data = v1 });
                r = await _db.tblKeyValue.GetAsync(myc, _db._identityId, k1);
                timers[i] = sw.ElapsedMilliseconds;
            }
        }

        await Task.Delay(1);

        return (0, timers);
    }
    
    private class RollbackException(string message) : Exception(message);
}

  