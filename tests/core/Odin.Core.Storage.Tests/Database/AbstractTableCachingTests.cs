using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database;

public class AbstractTableCachingTests : IocTestBase
{
    [Test]
    public async Task ItShouldNotBlowUpWhileDoingDeferredCommitActions()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var scopedConnectionFactory = scope.Resolve<ScopedIdentityConnectionFactory>();
        await using var cn = await scopedConnectionFactory.CreateScopedConnectionAsync();
        await using (var tx = await cn.BeginStackedTransactionAsync())
        {
            var tableAppNotificationsCached = scope.Resolve<TableAppNotificationsCached>();
            await tableAppNotificationsCached.DeleteAsync(Guid.NewGuid());
            await tableAppNotificationsCached.DeleteAsync(Guid.NewGuid());

            var tableAppGrantsCached = scope.Resolve<TableAppGrantsCached>();
            await tableAppGrantsCached.DeleteByIdentityAsync(Guid.NewGuid());
            await tableAppGrantsCached.DeleteByIdentityAsync(Guid.NewGuid());

            var tableKeyValueCached = scope.Resolve<TableKeyValueCached>();
            var k1 = Guid.NewGuid().ToByteArray();
            await tableKeyValueCached.DeleteAsync(k1);
            await tableKeyValueCached.DeleteAsync(k1);

            tx.Commit();
        }

        Assert.Pass();
    }

    //

    [Test]
    public async Task ItShouldInvalidateInAndAfterTransaction()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var db = scope.Resolve<IdentityDatabase>();

        var outerCache =  scope.Resolve<ITenantLevel2Cache>();
        var outerCacheInvalidationTag = new List<string> { TableKeyValueCached.RootInvalidationTag };
        var outerHit = false;

        var itemKey = Guid.NewGuid();
        var item = new KeyValueRecord { key = itemKey.ToByteArray(), data = Guid.NewGuid().ToByteArray() };

        var cacheKey = itemKey.ToString();

        {
            await using var tx = await db.BeginStackedTransactionAsync();

            // Outer cache MISS
            {
                outerHit = true;
                var data = await outerCache.GetOrSetAsync(
                    cacheKey,
                    _ =>
                    {
                        outerHit = false;
                        return db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(1000));
                    },
                    TimeSpan.FromMilliseconds(500),
                    EntrySize.Medium,
                    outerCacheInvalidationTag);

                Assert.That(data, Is.Null);
                Assert.That(outerHit, Is.False);
            }

            // Outer cache HIT
            {
                outerHit = true;
                var data = await outerCache.GetOrSetAsync(
                    cacheKey,
                    _ =>
                    {
                        outerHit = false;
                        return db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(1000));
                    },
                    TimeSpan.FromMilliseconds(500),
                    EntrySize.Medium,
                    outerCacheInvalidationTag);

                Assert.That(data, Is.Null);
                Assert.That(outerHit, Is.True);
            }

            // Insert data, invalidate outerCacheInvalidationTag
            await db.KeyValueCached.InsertAsync(item);

            // Outer cache MISS (because of invalidation when inserting)
            {
                outerHit = true;
                var data = await outerCache.GetOrSetAsync(
                    cacheKey,
                    _ =>
                    {
                        outerHit = false;
                        return db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(1000));
                    },
                    TimeSpan.FromMilliseconds(500),
                    EntrySize.Medium,
                    outerCacheInvalidationTag);

                // SEB:NOTE this fails because the cache key generated in ITenantLevel2Cache is not the same as the one
                // generated in TableKeyValueCached (bug)
                Assert.That(data, Is.Not.Null);
                Assert.That(outerHit, Is.True);
            }


            //
            // {
            //     // We're in a transaction, so cache was not updated in above INSERT
            //     var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
            //     Assert.That(r, Is.Not.Null);
            //     Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
            //     Assert.That(db.KeyValueCached.Misses, Is.EqualTo(0));
            // }
            //
            // {
            //     // We're in still a transaction, so cache was not updated in above INSERT
            //     var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
            //     Assert.That(r, Is.Not.Null);
            //     Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
            //     Assert.That(db.KeyValueCached.Misses, Is.EqualTo(0));
            // }
            //
            // tx.Commit();

            // NOTE: technically we're still in the transaction until leaving this scope
        }

        // {
        //     // MISS: we're not in a transaction, so cache is now accessed
        //     var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
        //     Assert.That(r, Is.Not.Null);
        //     Assert.That(db.KeyValueCached.Hits, Is.EqualTo(0));
        //     Assert.That(db.KeyValueCached.Misses, Is.EqualTo(1));
        // }
        //
        // {
        //     // HIT: we're not in a transaction, so cache is now accessed
        //     var r = await db.KeyValueCached.GetAsync(item.key, TimeSpan.FromMilliseconds(100));
        //     Assert.That(r, Is.Not.Null);
        //     Assert.That(db.KeyValueCached.Hits, Is.EqualTo(1));
        //     Assert.That(db.KeyValueCached.Misses, Is.EqualTo(1));
        // }

    }


}

