using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests;

public class TwoKeyValueStorageTests : IocTestBase
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        Assert.Throws<OdinSystemException>(() => { new TwoKeyValueStorage(Guid.Empty); });
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        var tblKeyTwoValue = Services.Resolve<TableKeyTwoValue>();

        var contextKey1 = Guid.NewGuid();
        var dataTypeKey = Guid.NewGuid().ToByteArray();
        var kvp1 = new TwoKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        await kvp1.UpsertAsync(tblKeyTwoValue, pk, dataTypeKey, expectedValue1);
        Assert.IsTrue(await kvp1.GetAsync<string>(tblKeyTwoValue, pk) == expectedValue1);

        await kvp1.DeleteAsync(tblKeyTwoValue, pk);
        Assert.IsTrue(await kvp1.GetAsync<string>(tblKeyTwoValue, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var kvp2 = new TwoKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        await kvp2.UpsertAsync(tblKeyTwoValue, pk, dataTypeKey, expectedValue2);
        Assert.IsTrue(await kvp2.GetAsync<string>(tblKeyTwoValue, pk) == expectedValue2);

        await kvp2.DeleteAsync(tblKeyTwoValue, pk);
        Assert.IsTrue(await kvp2.GetAsync<string>(tblKeyTwoValue, pk) == null);
    }
}
