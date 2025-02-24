using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Abstractions;

public class DatabaseConnectionTests : IocTestBase
{

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task RollbackTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var scopedIdentityConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedIdentityConnectionFactory.CreateScopedConnectionAsync();
        var tblKeyValue = scope.Resolve<TableKeyValue>();
        var identityKey = scope.Resolve<IdentityKey>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        await tblKeyValue.InsertAsync(new KeyValueRecord() { identityId = identityKey, key = k1, data = v1 });

        Assert.ThrowsAsync<RollbackException>(async () =>
        {
            await using var tx = await cn.BeginStackedTransactionAsync();
            await tblKeyValue.InsertAsync(new KeyValueRecord() { identityId = identityKey, key = k2, data = v2 });
            throw new RollbackException("rollback triggered");
        });

        var r = await tblKeyValue.GetAsync(k1);
        ClassicAssert.IsNotNull(r);

        r = await tblKeyValue.GetAsync(k2);
        ClassicAssert.IsNull(r);
    }


    /// <summary>
    /// The memory DB will become empty on the second connection
    /// while the first connection is still open
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task MemoryDatabaseDualConnectionTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r =await  tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

       await  tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
       await  tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r =await  tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        Thread.Sleep(1000);

        r =await  tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

    }


    /// <summary>
    /// Ensure that we can reuse prepared statments over two connections
    /// </summary>
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task DualConnectionPreparedStatementTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var k3 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();
        var v3 = Guid.NewGuid().ToByteArray();

        var r =await  tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await  tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await  tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await  tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        await  tblKeyValue.InsertAsync(new KeyValueRecord() { key = k3, data = v3 });

        r = await  tblKeyValue.GetAsync(k3);
        if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
            Assert.Fail();
    }



    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task MassivePreparedConnectionTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await PerformanceFramework.ThreadedTestAsync(20, 100, MassiveConnectionPreparedStatementTest);
    }

    private async Task<(long, long[])> MassiveConnectionPreparedStatementTest(int threadNo, int iterations)
    {
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();
        var identityKey = scope.Resolve<IdentityKey>();

        long[] timers = new long[iterations];
        var sw = new Stopwatch();

        for (int i=0; i < iterations; i++)
        {
            sw.Restart();
            var k1 = Guid.NewGuid().ToByteArray();
            var v1 = Guid.NewGuid().ToByteArray();

            var r = await tblKeyValue.GetAsync(k1);
            await tblKeyValue.InsertAsync(new KeyValueRecord() { identityId = identityKey, key = k1, data = v1 });
            r = await tblKeyValue.GetAsync(k1);
            timers[i] = sw.ElapsedMilliseconds;
        }

        await Task.Delay(1);

        return (0, timers);
    }
    
    private class RollbackException(string message) : Exception(message);
}
