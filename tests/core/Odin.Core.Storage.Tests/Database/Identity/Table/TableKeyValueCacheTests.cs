using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

#nullable enable

public class TableKeyValueCacheTests : IocTestBase
{
    [Test]
    [TestCase(DatabaseType.Sqlite)]
#if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
#endif
    public async Task ItShouldGetFromCache(DatabaseType databaseType)
    {
        await RegisterServicesAsync(databaseType);
        await using var scope = Services.BeginLifetimeScope();
        var tblKeyValueCache = scope.Resolve<TableKeyValueCache>();

        var k1 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();

        var record = await tblKeyValueCache.GetAsync(k1, Expiration.Sliding(TimeSpan.FromSeconds(1)));
        Assert.IsNull(record);

        record = new KeyValueRecord { key = k1, data = v1 };
        await tblKeyValueCache.InsertAsync(record, Expiration.Sliding(TimeSpan.FromSeconds(1)));

        record = await tblKeyValueCache.GetAsync(k1, Expiration.Sliding(TimeSpan.FromSeconds(1)));
        Assert.IsNotNull(record);

        var cache = scope.Resolve<IGenericMemoryCache<TableKeyValueCache>>();
        Assert.IsTrue(cache.Contains(k1));

        cache.Remove(k1);
        Assert.IsFalse(cache.Contains(k1));

        record = await tblKeyValueCache.GetAsync(k1, Expiration.Sliding(TimeSpan.FromSeconds(1)));
        Assert.IsNotNull(record);

        await tblKeyValueCache.DeleteAsync(k1);
        record = await tblKeyValueCache.GetAsync(k1, Expiration.Sliding(TimeSpan.FromSeconds(1)));
        Assert.IsNull(record);
    }

}