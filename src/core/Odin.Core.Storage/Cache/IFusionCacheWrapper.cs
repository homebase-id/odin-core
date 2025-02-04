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
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    TValue GetOrSet<TValue>(
        string key,
        Func<CancellationToken, TValue> factory,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    //
    // Setters
    //

    void Set<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
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