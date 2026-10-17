using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

/// <summary>
/// #1909: a reader misses the cache and loads the old value; a writer stores the new value and invalidates; then the
/// reader's fill lands. FusionCache dates an entry by when it was stored, so the late fill outlived the invalidation
/// and every later reader got the replaced value for the entry's whole TTL.
/// </summary>
public class TransactionalCacheStaleFillTests : IocTestBase
{
    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task AFillThatLoadedBeforeARemoveByTagDoesNotOutliveIt(bool redisEnabled)
    {
        var cache = await CreateCacheAsync(redisEnabled);
        await AssertLateFillIsNotKept(cache, c => c.CreateRemoveByTagsAction(Tags));
    }

    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task AFillThatLoadedBeforeARemoveByKeyDoesNotOutliveIt(bool redisEnabled)
    {
        var cache = await CreateCacheAsync(redisEnabled);
        await AssertLateFillIsNotKept(cache, c => c.CreateRemoveByKeyAction(Key));
    }

    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task AFillThatStartsAfterTheRemoveIsCached(bool redisEnabled)
    {
        var cache = await CreateCacheAsync(redisEnabled);
        await cache.InvalidateAsync([cache.CreateRemoveByTagsAction(Tags)]);

        var factoryCalls = 0;
        Task<string> Load(CancellationToken _)
        {
            factoryCalls++;
            return Task.FromResult("value");
        }

        await cache.GetOrSetAsync(Key, Load, TimeSpan.FromHours(1), tags: Tags);
        await cache.GetOrSetAsync(Key, Load, TimeSpan.FromHours(1), tags: Tags);

        Assert.That(factoryCalls, Is.EqualTo(1), "a fill that read after the last remove must be kept");
    }

    [Test]
    [TestCase(false)]
#if RUN_REDIS_TESTS
    [TestCase(true)]
#endif
    public async Task ASetOfAValueReadBeforeARemoveIsNotKept(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        var cache = Services.Resolve<ITenantLevel2Cache>();

        var raced = cache.BeginRead(); // the read, before the write below
        await cache.RemoveByTagAsync(Tags); // the write's invalidation
        await cache.SetIfNotRemovedSinceAsync("raced", "old", raced, TimeSpan.FromHours(1), tags: Tags);
        Assert.That((await cache.TryGetAsync<string>("raced")).HasValue, Is.False,
            "a value read before the remove must not be stored after it");

        var fresh = cache.BeginRead();
        await cache.SetIfNotRemovedSinceAsync("fresh", "new", fresh, TimeSpan.FromHours(1), tags: Tags);
        Assert.That((await cache.TryGetAsync<string>("fresh")).Value, Is.EqualTo("new"),
            "a value read after the last remove is stored");
    }

    //

    private const string Key = "key";
    private static readonly List<string> Tags = ["row"];

    private async Task<TransactionalCache> CreateCacheAsync(bool redisEnabled)
    {
        await RegisterServicesAsync(DatabaseType.Sqlite, redisEnabled: redisEnabled);
        var scope = Services.BeginLifetimeScope();
        return scope.Resolve<IIdentityTransactionalCacheFactory>().Create("stale-fill", "stale-fill-root");
    }

    private static async Task AssertLateFillIsNotKept(TransactionalCache cache, Func<TransactionalCache, Func<Task>> remove)
    {
        var stored = "old";
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFill = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var slowReader = cache.GetOrSetAsync(Key, async _ =>
        {
            var value = stored; // the database read, before the write below
            loaded.SetResult();
            await releaseFill.Task;
            return value;
        }, TimeSpan.FromHours(1), tags: Tags);

        await loaded.Task;
        stored = "new"; // the write commits...
        await cache.InvalidateAsync([remove(cache)]); // ...and invalidates, after the read
        releaseFill.SetResult();
        Assert.That(await slowReader, Is.EqualTo("old"), "the slow reader itself still answers what it read");

        var next = await cache.GetOrSetAsync(Key, _ => Task.FromResult(stored), TimeSpan.FromHours(1), tags: Tags);
        Assert.That(next, Is.EqualTo("new"), "a reader after the write must not get the value the write replaced");
    }
}
