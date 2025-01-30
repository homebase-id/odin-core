using System;
using System.Threading;
using System.Threading.Tasks;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

public abstract class FusionCacheWrapper(CacheKeyPrefix prefix, IFusionCache cache) : IFusionCacheWrapper
{
    protected abstract FusionCacheEntryOptions DefaultOptions { get; }

    //

    public TValue GetOrDefault<TValue>(
        string key,
        TValue defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrDefault(
            prefix + key,
            defaultValue,
            options,
            cancellationToken);
    }

    //

    public ValueTask<TValue> GetOrDefaultAsync<TValue>(
        string key,
        TValue defaultValue = default,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrDefaultAsync(
            prefix + key,
            defaultValue,
            options,
            cancellationToken);
    }

    //

    public MaybeValue<TValue> TryGet<TValue>(string key, CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.TryGet<TValue>(
            prefix + key,
            options,
            cancellationToken);
    }

    //

    public ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.TryGetAsync<TValue>(
            prefix + key,
            options,
            cancellationToken);
    }

    //

    public TValue GetOrSet<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrSet(
            prefix + key,
            defaultValue,
            options,
            tags: null,
            cancellationToken);
    }

    //

    public ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        TValue defaultValue,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrSetAsync(
            prefix + key,
            defaultValue,
            options,
            tags: null,
            cancellationToken);
    }

    //

    public TValue GetOrSet<TValue>(
        string key, Func<CancellationToken, TValue> factory,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrSet(
            prefix + key,
            factory,
            options,
            cancellationToken);
    }

    //

    public ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.GetOrSetAsync(
            prefix + key,
            factory,
            options,
            cancellationToken);
    }

    //

    public void Set<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate(duration);

        cache.Set(
            prefix + key,
            value,
            options,
            cancellationToken);
    }

    //

    public ValueTask SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate(duration);

        return cache.SetAsync(
            prefix + key,
            value,
            options,
            cancellationToken);
    }

    //

    public void Remove(string key, CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        cache.Remove(
            prefix + key,
            options,
            cancellationToken);
    }

    //

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var options = DefaultOptions.Duplicate();

        return cache.RemoveAsync(
            prefix + key,
            options,
            cancellationToken);
    }

    //

}