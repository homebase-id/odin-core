using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage.Cache;
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

        var record = await tblKeyValueCache.GetAsync(k1, TimeSpan.FromSeconds(1));
        ClassicAssert.IsNull(record);

        record = new KeyValueRecord { key = k1, data = v1 };
        await tblKeyValueCache.InsertAsync(record, TimeSpan.FromSeconds(1));

        record = await tblKeyValueCache.GetAsync(k1, TimeSpan.FromSeconds(1));
        ClassicAssert.IsNotNull(record);

        var cache = scope.Resolve<ITenantLevel1Cache<TableKeyValueCache>>();
        ClassicAssert.IsTrue(await cache.ContainsAsync(TableKeyValueCache.CacheKey(k1)));

        await cache.RemoveAsync(TableKeyValueCache.CacheKey(k1));
        ClassicAssert.IsFalse(await cache.ContainsAsync(TableKeyValueCache.CacheKey(k1)));

        record = await tblKeyValueCache.GetAsync(k1, TimeSpan.FromSeconds(1));
        ClassicAssert.IsNotNull(record);

        await tblKeyValueCache.DeleteAsync(k1);
        record = await tblKeyValueCache.GetAsync(k1, TimeSpan.FromSeconds(1));
        ClassicAssert.IsNull(record);
    }

}