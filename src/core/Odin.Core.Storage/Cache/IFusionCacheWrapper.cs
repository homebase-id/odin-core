using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

#nullable enable

public interface IFusionCacheWrapper
{
    //
    // Keys
    //

    string CacheKeyPrefix { get; }

    //
    // Getters
    //

    TValue? GetOrDefault<TValue>(
        string key,
        TValue? defaultValue = default,
        CancellationToken cancellationToken = default);

    ValueTask<TValue?> GetOrDefaultAsync<TValue>(
        string key,
        TValue? defaultValue = default,
        CancellationToken cancellationToken = default);

    MaybeValue<TValue> TryGet<TValue>(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(
        string key,
        CancellationToken cancellationToken = default);

    //
    // GettersSetters
    //

    TValue GetOrSet<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    TValue GetOrSet<TValue>(
        string key,
        Func<CancellationToken, TValue> factory,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        Func<TValue, long> entrySizeFactory,
        IEnumerable<string>? tags = null,
        Func<TValue, bool>? storeIf = null,
        CancellationToken cancellationToken = default);

    //
    // Setters
    //

    void Set<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Where the cache's remove count stood when a read that a later <see cref="SetIfNotRemovedSinceAsync{TValue}"/>
    /// stores started
    /// </summary>
    ValueTask<CacheReadStart> BeginReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value read since <paramref name="readStarted"/>, unless this cache has removed anything since: the
    /// removed entry may be what was read (#1909). For a caller that reads the cache, builds the value itself and
    /// then sets it, instead of using a factory GetOrSet, which does this already.
    /// </summary>
    ValueTask SetIfNotRemovedSinceAsync<TValue>(
        string key,
        TValue value,
        CacheReadStart readStarted,
        TimeSpan duration,
        long entrySize = EntrySize.Small,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    //
    // Removers
    //

    void Remove(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(
        string key,
        CancellationToken cancellationToken = default);

    void RemoveByTag(
        string tag,
        CancellationToken cancellationToken = default);

    ValueTask RemoveByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);

    void RemoveByTag(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);

    ValueTask RemoveByTagAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default);

    //
    // Checkers
    //
    bool Contains(string key, CancellationToken cancellationToken = default);
    ValueTask<bool> ContainsAsync(string key, CancellationToken cancellationToken = default);
}