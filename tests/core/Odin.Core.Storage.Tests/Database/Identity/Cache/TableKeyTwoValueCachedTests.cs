using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableKeyTwoValueCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyTwoValueCached = scope.Resolve<TableKeyTwoValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();

        {
            var record = await tableKeyTwoValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(1));
        }

        {
            var record = await tableKeyTwoValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(1));
        }

        {
            var records = await tableKeyTwoValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(2));
        }

        {
            var records = await tableKeyTwoValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(2));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(2));
        }

        var item = new KeyTwoValueRecord { key1 = k1, key2 = k2, data = Guid.NewGuid().ToByteArray() };
        await tableKeyTwoValueCached.UpsertAsync(item);

        {
            var record = await tableKeyTwoValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(2));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(3));
        }

        {
            var record = await tableKeyTwoValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(3));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(3));
        }

        {
            var records = await tableKeyTwoValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(3));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(4));
        }

        {
            var records = await tableKeyTwoValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyTwoValueCached.Hits, Is.EqualTo(4));
            Assert.That(tableKeyTwoValueCached.Misses, Is.EqualTo(4));
        }

    }


    //

}


