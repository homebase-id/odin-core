using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Cache;

public class TableKeyValueCachedTests : IocTestBase
{
    [Test]
    public async Task ItShould_HitAndMissNullValues_WhenGettingNonExistingRecords()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        await Task.Delay(200);

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(2));
        }
    }

    //

    [Test]
    public async Task ItShould_HitAndMissRecords_WhenGettingExistingRecords()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.InsertAsync(
                new KeyValueRecord { key = k1, data = v1 },
                TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.key, Is.EqualTo(k1));
            Assert.That(r.data, Is.EqualTo(v1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        await Task.Delay(200);

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.key, Is.EqualTo(k1));
            Assert.That(r.data, Is.EqualTo(v1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(2));
        }
    }

    //

    [Test]
    public async Task ItShould_HitAndMissRecords_WhenGettingExistingRecordThatIsDeleted()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.InsertAsync(
                new KeyValueRecord { key = k1, data = v1 },
                TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.key, Is.EqualTo(k1));
            Assert.That(r.data, Is.EqualTo(v1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            await tableKeyValueCached.DeleteAsync(k1);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(2));
        }
    }

    //

    [Test]
    public async Task ItShould_HitAndMissRecords_WhenInvalidatingAll()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();

        var k1 = Guid.NewGuid().ToByteArray();
        var v1 = Guid.NewGuid().ToByteArray();

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Null);
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.InsertAsync(
                new KeyValueRecord { key = k1, data = v1 },
                TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.key, Is.EqualTo(k1));
            Assert.That(r.data, Is.EqualTo(v1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            await tableKeyValueCached.InvalidateAllAsync();
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            var r = await tableKeyValueCached.GetAsync(k1, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.key, Is.EqualTo(k1));
            Assert.That(r.data, Is.EqualTo(v1));
            Assert.That(tableKeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(tableKeyValueCached.Misses, Is.EqualTo(2));
        }
    }


}