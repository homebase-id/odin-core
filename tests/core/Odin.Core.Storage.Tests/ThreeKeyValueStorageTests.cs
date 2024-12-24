using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests;

public class ThreeKeyValueStorageTests : IocTestBase
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        Assert.Throws<OdinSystemException>(() => { new ThreeKeyValueStorage(Guid.Empty); });
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        var tblKeyThreeValue = Services.Resolve<TableKeyThreeValue>();

        var contextKey1 = Guid.NewGuid();
        var dataTypeKey = Guid.NewGuid().ToByteArray();
        var dataCategoryKey = Guid.NewGuid().ToByteArray();
        var kvp1 = new ThreeKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        await kvp1.UpsertAsync(tblKeyThreeValue, pk, dataTypeKey, dataCategoryKey, expectedValue1);
        Assert.IsTrue(await kvp1.GetAsync<string>(tblKeyThreeValue, pk) == expectedValue1);

        await kvp1.DeleteAsync(tblKeyThreeValue, pk);
        Assert.IsTrue(await kvp1.GetAsync<string>(tblKeyThreeValue, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var kvp2 = new ThreeKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        await kvp2.UpsertAsync(tblKeyThreeValue, pk, dataTypeKey, dataCategoryKey, expectedValue2);
        Assert.IsTrue(await kvp2.GetAsync<string>(tblKeyThreeValue, pk) == expectedValue2);

        await kvp2.DeleteAsync(tblKeyThreeValue, pk);
        Assert.IsTrue(await kvp2.GetAsync<string>(tblKeyThreeValue, pk) == null);
    }
}