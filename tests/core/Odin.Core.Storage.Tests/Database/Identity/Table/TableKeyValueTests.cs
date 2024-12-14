using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableKeyValueTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task InsertTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();
    }


    // Test that inserting a duplicate throws an exception
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task InsertDuplicateTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

        bool ok = false;

        try
        {
            await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v2 });
            ok = true;
        }
        catch
        {
            ok = false;
        }

        Debug.Assert(ok == false);

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task UpdateTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k1, data = v2 });

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
            Assert.Fail();
    }


    // Test updating non existing row just continues
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task Update2Test(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

        bool ok = false;

        try
        {
            await tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k2, data = v2 });
            ok = true;
        }
        catch
        {
            ok = false;
        }

        Debug.Assert(ok == true);

    }



    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task DeleteTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        await tblKeyValue.DeleteAsync(k1);
        r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);
    }


    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task UpsertTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();
        var v3 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        Debug.Assert(r == null);

        await tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        r = await tblKeyValue.GetAsync(k2);
        if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
            Assert.Fail();

        await tblKeyValue.UpsertAsync(new KeyValueRecord() { key = k2, data = v3 });

        r = await tblKeyValue.GetAsync(k2);
        if (ByteArrayUtil.muidcmp(r.data, v3) != 0)
            Assert.Fail();

    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task CreateTableTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });

        var r = await tblKeyValue.GetAsync(k1);

        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await tblKeyValue.GetAsync(k1);

        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        r = await tblKeyValue.GetAsync(k2);

        if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
            Assert.Fail();

        await tblKeyValue.UpdateAsync(new KeyValueRecord() { key = k2, data = v1 });

        r = await tblKeyValue.GetAsync(k2);

        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();

        await tblKeyValue.DeleteAsync(k2);

        r = await tblKeyValue.GetAsync(k2);

        if (r != null)
            Assert.Fail();

    }


    // Test inserting two row´s in a transaction and reading their values
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task CommitTest(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValue = scope.Resolve<TableKeyValue>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();
        var v2 = Guid.NewGuid().ToByteArray();

        var r = await tblKeyValue.GetAsync(k1);
        if (r != null)
            Assert.Fail();
        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k1, data = v1 });
        await tblKeyValue.InsertAsync(new KeyValueRecord() { key = k2, data = v2 });

        r = await tblKeyValue.GetAsync(k1);
        if (ByteArrayUtil.muidcmp(r.data, v1) != 0)
            Assert.Fail();
        r = await tblKeyValue.GetAsync(k2);
        if (ByteArrayUtil.muidcmp(r.data, v2) != 0)
            Assert.Fail();
    }
}