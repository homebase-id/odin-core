using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Tests;

public class SingleKeyValueStorageTests : IocTestBase
{
    [Test]
    public void RequireNonEmptyContextKey()
    {
        Assert.Throws<OdinSystemException>(() => { new SingleKeyValueStorage(Guid.Empty); });
    }

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task CanGetCorrectValueUsing_DuplicatePrimaryKey_WithDifferentContextKey(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        var tblKeyValue = Services.Resolve<TableKeyValue>();

        var contextKey1 = Guid.NewGuid();
        var singleKvp1 = new SingleKeyValueStorage(contextKey1);

        var pk = Guid.Parse("a6e58b87-e65b-4d98-8060-eb783079b267");

        const string expectedValue1 = "some value";
        await singleKvp1.UpsertAsync(tblKeyValue, pk, expectedValue1);
        Assert.IsTrue(await singleKvp1.GetAsync<string>(tblKeyValue, pk) == expectedValue1);
        await singleKvp1.DeleteAsync(tblKeyValue, pk);
        Assert.IsTrue(await singleKvp1.GetAsync<string>(tblKeyValue, pk) == null);

        var contextKey2 = Guid.NewGuid();
        var singleKvp2 = new SingleKeyValueStorage(contextKey2);
        const string expectedValue2 = "another value";
        await singleKvp2.UpsertAsync(tblKeyValue, pk, expectedValue2);
        Assert.IsTrue(await singleKvp2.GetAsync<string>(tblKeyValue, pk) == expectedValue2);

        await singleKvp2.DeleteAsync(tblKeyValue, pk);
        Assert.IsTrue(await singleKvp2.GetAsync<string>(tblKeyValue, pk) == null);
    }
}