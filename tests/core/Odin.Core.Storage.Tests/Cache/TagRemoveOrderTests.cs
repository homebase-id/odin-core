using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

/// <summary>
/// Guards the wrapper's turns for removes of one tag (<see cref="FusionCacheWrapper"/>): a remove that stalls between
/// dating itself and storing the date must not bring back an entry a later remove expired. The stall is a memory cache
/// that holds the write of the tag's date until released.
/// </summary>
public class TagRemoveOrderTests
{
    [Test]
    public async Task AStalledTagRemoveDoesNotBringBackAnEntryALaterRemoveExpired()
    {
        var memory = new StallingMemoryCache();
        var services = new ServiceCollection();
        services.AddCoreCacheServices(new CacheConfiguration
        {
            MemoryCacheSizeLimit = long.MaxValue,
            MemoryCacheCompactionPercentage = 0.25,
            Level2CacheType = Level2CacheType.None
        }, memory);
        await using var provider = services.BuildServiceProvider();
        var cache = new TenantLevel1Cache(new CacheKeyPrefix("tenant"), provider.GetRequiredService<IFusionCache>(),
            provider.GetRequiredService<IRemovalCounterFactory>());
        List<string> tags = ["drive"];

        // The first remove has its date and stalls before storing it
        memory.StallNextTagWrite();
        var stalledRemove = Task.Run(() => cache.RemoveByTagAsync(tags).AsTask());
        await memory.Stalled;

        // An entry stored after that date, which the second remove must expire
        var stored = await cache.GetOrSetAsync("row", _ => Task.FromResult("read between the removes"),
            TimeSpan.FromHours(1), tags: tags);
        Assert.That(stored, Is.EqualTo("read between the removes"));

        var laterRemove = Task.Run(() => cache.RemoveByTagAsync(tags).AsTask());
        await Task.WhenAny(laterRemove, Task.Delay(TimeSpan.FromMilliseconds(500))); // left to finish first, if it may
        memory.ReleaseStall();
        await Task.WhenAll(stalledRemove, laterRemove);

        var read = await cache.GetOrSetAsync("row", _ => Task.FromResult("read after both removes"),
            TimeSpan.FromHours(1), tags: tags);
        Assert.That(read, Is.EqualTo("read after both removes"),
            "an entry the later remove expired must stay expired once the stalled one lands");
    }

    // A memory cache whose next write of a tag's date blocks until released: FusionCache has dated the remove by then
    private sealed class StallingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions { SizeLimit = long.MaxValue });
        private readonly ManualResetEventSlim _release = new();
        private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;

        public Task Stalled => _stalled.Task;
        public void StallNextTagWrite() => Interlocked.Exchange(ref _armed, 1);
        public void ReleaseStall() => _release.Set();

        public ICacheEntry CreateEntry(object key)
        {
            if (key is string text && text.Contains("__fc:t:") && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                _stalled.SetResult();
                _release.Wait(TimeSpan.FromSeconds(30));
            }
            return _inner.CreateEntry(key);
        }

        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);
        public void Remove(object key) => _inner.Remove(key);

        public void Dispose()
        {
            _inner.Dispose();
            _release.Dispose();
        }
    }
}
