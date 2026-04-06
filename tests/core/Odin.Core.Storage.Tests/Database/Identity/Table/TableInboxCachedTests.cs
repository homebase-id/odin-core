using System;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Database.Identity.Table;

public class TableInboxCachedTests : IocTestBase
{
    [Test]
    public async Task ItShouldCacheGetReadyCount()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableInboxCached = scope.Resolve<TableInboxCached>();

        var boxId = Guid.NewGuid();
        var f1 = SequentialGuid.CreateGuid();
        var f2 = SequentialGuid.CreateGuid();
        var v1 = SequentialGuid.CreateGuid().ToByteArray();

        //
        // Cold miss: first call to GetReadyCountAsync goes to DB
        //

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(0));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(1));
        }

        //
        // Cache hit: second call returns from cache
        //

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(1));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(1));
        }

        //
        // Insert invalidates the cache
        //

        await tableInboxCached.InsertAsync(new InboxRecord()
        {
            boxId = boxId, fileId = f1, priority = 0, value = v1
        });

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(1));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(2));
        }

        //
        // Cache hit after insert
        //

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(2));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(2));
        }

        //
        // Insert a second item, invalidates again
        //

        await tableInboxCached.InsertAsync(new InboxRecord()
        {
            boxId = boxId, fileId = f2, priority = 1, value = v1
        });

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(2));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(2));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(3));
        }

        //
        // Pop one item invalidates the cache
        //

        var popped = await tableInboxCached.PopSpecificBoxAsync(boxId, 1);
        Assert.That(popped.Count, Is.EqualTo(1));

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(2));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(4));
        }

        //
        // Commit the popped item (delete from inbox) invalidates the cache
        //

        await tableInboxCached.PopCommitListAsync((Guid)popped[0].popStamp!, boxId, [popped[0].fileId]);

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(1));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(2));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(5));
        }

        //
        // Pop and commit the last item
        //

        var popped2 = await tableInboxCached.PopSpecificBoxAsync(boxId, 1);
        await tableInboxCached.PopCommitListAsync((Guid)popped2[0].popStamp!, boxId, [popped2[0].fileId]);

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(2));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(6)); // pop + commit both invalidate, but only 1 query = 1 miss
        }

        //
        // Back to zero, cache hit
        //

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(0));
            Assert.That(tableInboxCached.Hits, Is.EqualTo(3));
            Assert.That(tableInboxCached.Misses, Is.EqualTo(6));
        }
    }

    [Test]
    public async Task ItShouldIsolateCacheByBoxId()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableInboxCached = scope.Resolve<TableInboxCached>();

        var box1 = Guid.NewGuid();
        var box2 = Guid.NewGuid();
        var v1 = SequentialGuid.CreateGuid().ToByteArray();

        // Insert into box1 only
        await tableInboxCached.InsertAsync(new InboxRecord()
        {
            boxId = box1, fileId = SequentialGuid.CreateGuid(), priority = 0, value = v1
        });

        {
            var count1 = await tableInboxCached.GetReadyCountAsync(box1, TimeSpan.FromSeconds(10));
            Assert.That(count1, Is.EqualTo(1));
        }

        {
            var count2 = await tableInboxCached.GetReadyCountAsync(box2, TimeSpan.FromSeconds(10));
            Assert.That(count2, Is.EqualTo(0));
        }

        // Insert into box2 should not affect box1's cache
        await tableInboxCached.InsertAsync(new InboxRecord()
        {
            boxId = box2, fileId = SequentialGuid.CreateGuid(), priority = 0, value = v1
        });

        {
            // box1 should still be a cache hit with count=1
            var count1 = await tableInboxCached.GetReadyCountAsync(box1, TimeSpan.FromSeconds(10));
            Assert.That(count1, Is.EqualTo(1));
            // box2 should be a miss (invalidated by its own insert)
            var count2 = await tableInboxCached.GetReadyCountAsync(box2, TimeSpan.FromSeconds(10));
            Assert.That(count2, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ItShouldInvalidateOnPopCancel()
    {
        await RegisterServicesAsync(DatabaseType.Sqlite);
        await using var scope = Services.BeginLifetimeScope();
        var tableInboxCached = scope.Resolve<TableInboxCached>();

        var boxId = Guid.NewGuid();
        var v1 = SequentialGuid.CreateGuid().ToByteArray();
        var f1 = SequentialGuid.CreateGuid();

        await tableInboxCached.InsertAsync(new InboxRecord()
        {
            boxId = boxId, fileId = f1, priority = 0, value = v1
        });

        // Pop the item (ready count goes to 0)
        var popped = await tableInboxCached.PopSpecificBoxAsync(boxId, 1);

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(0));
        }

        // Cancel the pop (item goes back to ready)
        await tableInboxCached.PopCancelListAsync((Guid)popped[0].popStamp!, boxId, [f1]);

        {
            var count = await tableInboxCached.GetReadyCountAsync(boxId, TimeSpan.FromSeconds(10));
            Assert.That(count, Is.EqualTo(1));
        }
    }
}
