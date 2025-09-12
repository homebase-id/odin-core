using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableKeyThreeValueCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldTestCachingFromAtoZ()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyThreeValueCached = scope.Resolve<TableKeyThreeValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();
        var k2 = Guid.NewGuid().ToByteArray();
        var k3 = Guid.NewGuid().ToByteArray();

        {
            var record = await tableKeyThreeValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(1));
        }

        {
            var record = await tableKeyThreeValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Null);
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(1));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(2));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(2));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(2));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyThreeAsync(k3, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(2));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(3));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyThreeAsync(k3, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(0));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(3));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(3));
        }

        var item = new KeyThreeValueRecord { key1 = k1, key2 = k2, key3 = k3, data = Guid.NewGuid().ToByteArray() };
        await tableKeyThreeValueCached.UpsertAsync(item);

        {
            var record = await tableKeyThreeValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(3));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(4));
        }

        {
            var record = await tableKeyThreeValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(record, Is.Not.Null);
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(4));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(4));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(4));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(5));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyTwoAsync(k2, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(5));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(5));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyThreeAsync(k3, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(5));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(6));
        }

        {
            var records = await tableKeyThreeValueCached.GetByKeyThreeAsync(k3, TimeSpan.FromMilliseconds(100));
            Assert.That(records.Count, Is.EqualTo(1));
            Assert.That(tableKeyThreeValueCached.Hits, Is.EqualTo(6));
            Assert.That(tableKeyThreeValueCached.Misses, Is.EqualTo(6));
        }


    }


    //

}


