using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

#nullable enable

public abstract class FusionCacheWrapper(string cacheKeyPrefix, IFusionCache cache) : IFusionCacheWrapper
{
    protected abstract FusionCacheEntryOptions DefaultEntryOptions { get; }

    //

    // When this cache last removed anything, in Stopwatch ticks (monotonic, unaffected by clock changes). FusionCache
    // keeps an entry stored after a remove, even when its factory read the database before the write that removed it
    // (#1909): the entry's timestamp is when it was stored, not when it was read. So a factory notes when it started,
    // and an answer that started before the last remove is not kept. Every entry and tag carries this wrapper's
    // prefix, so only this wrapper can remove what it stored, and one value per wrapper (one per tenant) is enough.
    private long _lastRemoval;

    private void MarkRemoval() => Interlocked.Exchange(ref _lastRemoval, Stopwatch.GetTimestamp());

    private bool RemovedSince(long fillStarted) => Interlocked.Read(ref _lastRemoval) >= fillStarted;

    // Inside a factory: return the value without storing it, and without telling other nodes of a write that did not happen
    private static void SkipWrites<TValue>(FusionCacheFactoryExecutionContext<TValue> ctx)
    {
        ctx.Options.SkipMemoryCacheWrite = true;
        ctx.Options.SkipDistributedCacheWrite = true;
        ctx.Options.SkipBackplaneNotifications = true;
    }

    // After a fill: a remove may have landed between the factory's check and FusionCache's store. Removing what was
    // just stored costs at most a miss. It runs after GetOrSet returned, outside FusionCache's per-key factory lock.
    private bool RemovedDuringFill(long fillStarted) => fillStarted != 0 && RemovedSince(fillStarted);

    //

    public string CacheKeyPrefix => cacheKeyPrefix;

    //

    /// <summary>
    /// The shortest lifetime an entry can be given. Shorter is not "cache briefly", it is an error
    /// (see <see cref="GuardDurationForL2"/>), so a caller with less time than this left should not
    /// cache at all.
    /// </summary>
    public static readonly TimeSpan MinL2Duration = TimeSpan.FromSeconds(2);
    private void GuardDurationForL2(TimeSpan duration, FusionCacheEntryOptions options)
    {
        if (duration < MinL2Duration)
        {
            throw new OdinSystemException(
                $"Cache duration {duration.TotalMilliseconds}ms is too short when writing to the distributed (L2) " +
                $"cache. Microsoft.Extensions.Caching.StackExchangeRedis truncates TTL to whole seconds via " +
                $"(long)TotalSeconds, so any sub-second value becomes EXPIRE 0 and Redis deletes the key " +
                $"immediately. Use a duration >= {MinL2Duration.TotalSeconds}s. See " +
                "https://github.com/dotnet/aspnetcore/blob/8ee12ef7a2c179f3d7c7da5ab33d76d652042d0b/src/Caching/StackExchangeRedis/src/RedisCache.cs#L586");
        }
    }

    //

    public TValue? GetOrDefault<TValue>(
        string key,
        TValue? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate();
        return cache.GetOrDefault(
            AddPrefix(key),
            defaultValue,
            options,
            cancellationToken);
    }

    //

    public ValueTask<TValue?> GetOrDefaultAsync<TValue>(
        string key,
        TValue? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate();
        return cache.GetOrDefaultAsync(
            AddPrefix(key),
            defaultValue,
            options,
            cancellationToken);
    }

    //

    public MaybeValue<TValue> TryGet<TValue>(string key, CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate();
        return cache.TryGet<TValue>(
            AddPrefix(key),
            options,
            cancellationToken);
    }

    //

    public ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate();
        return cache.TryGetAsync<TValue>(
            AddPrefix(key),
            options,
            cancellationToken);
    }

    //

    public TValue GetOrSet<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);
        options.Size = entrySize;

        GuardDurationForL2(duration, options);

        return cache.GetOrSet(
            AddPrefix(key),
            defaultValue,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);
        options.Size = entrySize;

        GuardDurationForL2(duration, options);

        return cache.GetOrSetAsync(
            AddPrefix(key),
            defaultValue,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public TValue GetOrSet<TValue>(
        string key,
        Func<CancellationToken, TValue> factory,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);
        options.Size = entrySize;

        GuardDurationForL2(duration, options);

        long fillStarted = 0;
        var result = cache.GetOrSet<TValue>(
            AddPrefix(key),
            (ctx, ct) =>
            {
                fillStarted = Stopwatch.GetTimestamp();
                var value = factory(ct);
                if (RemovedSince(fillStarted))
                {
                    SkipWrites(ctx);
                }
                return value;
            },
            options,
            tags: AddPrefix(tags),
            cancellationToken);

        if (RemovedDuringFill(fillStarted))
        {
            cache.Remove(AddPrefix(key), DefaultEntryOptions.Duplicate(), cancellationToken);
        }

        return result;
    }

    //

    public ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        return GetOrSetAsync(key, factory, duration, _ => entrySize, tags, storeIf: null, cancellationToken);
    }

    //

    public async ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        Func<TValue, long> entrySizeFactory,
        IEnumerable<string>? tags = null,
        Func<TValue, bool>? storeIf = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);

        GuardDurationForL2(duration, options);

        long fillStarted = 0;
        var result = await cache.GetOrSetAsync<TValue>(
            AddPrefix(key),
            async (ctx, ct) =>
            {
                fillStarted = Stopwatch.GetTimestamp();
                var value = await factory(ct);
                ctx.Options.Size = entrySizeFactory(value);
                if ((storeIf != null && !storeIf(value)) || RemovedSince(fillStarted))
                {
                    SkipWrites(ctx);
                }
                return value;
            },
            options,
            tags: AddPrefix(tags),
            cancellationToken);

        if (RemovedDuringFill(fillStarted))
        {
            await cache.RemoveAsync(AddPrefix(key), DefaultEntryOptions.Duplicate(), cancellationToken);
        }

        return result;
    }

    //

    public void Set<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);
        options.Size = entrySize;

        GuardDurationForL2(duration, options);

        cache.Set(
            AddPrefix(key),
            value,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        CacheTypeGuard<TValue>.EnsureValid();

        var options = DefaultEntryOptions.Duplicate(duration);
        options.Size = entrySize;

        GuardDurationForL2(duration, options);

        return cache.SetAsync(
            AddPrefix(key),
            value,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public void Remove(string key, CancellationToken cancellationToken = default)
    {
        MarkRemoval();
        var options = DefaultEntryOptions.Duplicate();

        cache.Remove(
            AddPrefix(key),
            options,
            cancellationToken);
    }

    //

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        MarkRemoval();
        var options = DefaultEntryOptions.Duplicate();

        return cache.RemoveAsync(
            AddPrefix(key),
            options,
            cancellationToken);
    }
    
    //

    public void RemoveByTag(string tag, CancellationToken cancellationToken = default)
    {
        MarkRemoval();
        cache.RemoveByTag(
            AddPrefix(tag),
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }
    
    //

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        MarkRemoval();
        return cache.RemoveByTagAsync(
            AddPrefix(tag),
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }

    //

    public void RemoveByTag(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags, nameof(tags));

        MarkRemoval();
        cache.RemoveByTag(
            AddPrefix(tags)!,
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }

    //

    public ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags, nameof(tags));

        MarkRemoval();
        return cache.RemoveByTagAsync(
            AddPrefix(tags)!,
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }

    //

    public bool Contains(string key, CancellationToken cancellationToken = default)
    {
        var value = TryGet<object>(key, cancellationToken);
        return value.HasValue;
    }

    //

    public async ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default)
    {
        var value = await TryGetAsync<object>(key, cancellationToken);
        return value.HasValue;
    }

    //

    private string AddPrefix(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text, nameof(text));
        ArgumentException.ThrowIfNullOrEmpty(cacheKeyPrefix, nameof(cacheKeyPrefix));
        return cacheKeyPrefix + ":" + text;
    }

    //

    private List<string>? AddPrefix(IEnumerable<string>? list)
    {
        if (list == null)
        {
            return null;
        }

        var result = new List<string>(list is ICollection<string> col ? col.Count : 10);
        foreach (var text in list)
        {
            result.Add(AddPrefix(text));
        }

        return result;
    }

    //

}
