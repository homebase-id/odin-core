using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
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

    //

    [Test]
    public async Task ItShould_SkipCacheUpdates_InTransactions()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var item = new KeyValueRecord { key = Guid.NewGuid().ToByteArray(), data = Guid.NewGuid().ToByteArray() };

        {
            await using var tx = await db.BeginStackedTransactionAsync();
            await db.KeyValueCached.InsertAsync(item, TimeSpan.FromMilliseconds(100));

            {
                // We're in a transaction, so cache was not updated in above INSERT
                var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
                Assert.That(r, Is.Not.Null);
                Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
                Assert.That(db.KeyValueCached.Misses, Is.EqualTo(0));
            }

            {
                // We're in still a transaction, so cache was not updated in above INSERT
                var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
                Assert.That(r, Is.Not.Null);
                Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
                Assert.That(db.KeyValueCached.Misses, Is.EqualTo(0));
            }

            tx.Commit();

            // NOTE: technically we're still in the transaction until leaving this scope
        }

        {
            // MISS: we're not in a transaction, so cache is now accessed
            var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
            Assert.That(db.KeyValueCached.Misses, Is.EqualTo(1));
        }

        {
            // HIT: we're not in a transaction, so cache is now accessed
            var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
            Assert.That(r, Is.Not.Null);
            Assert.That(db.KeyValueCached.Hits, Is.EqualTo(1));
            Assert.That(db.KeyValueCached.Misses, Is.EqualTo(1));
        }
    }

}