using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Cache;

#nullable enable

public abstract class FusionCacheWrapper(string cacheKeyPrefix, IFusionCache cache) : IFusionCacheWrapper
{
    protected abstract FusionCacheEntryOptions DefaultEntryOptions { get; }

    //

    public string CacheKeyPrefix => cacheKeyPrefix;

    //

    public TValue? GetOrDefault<TValue>(
        string key,
        TValue? defaultValue = default,
        CancellationToken cancellationToken = default)
    {
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
        var options = DefaultEntryOptions.Duplicate();

        return cache.TryGet<TValue>(
            AddPrefix(key),
            options,
            cancellationToken);
    }

    //

    public ValueTask<MaybeValue<TValue>> TryGetAsync<TValue>(string key, CancellationToken cancellationToken = default)
    {
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
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

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
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

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
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

        return cache.GetOrSet<TValue>(
            AddPrefix(key),
            factory,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public ValueTask<TValue> GetOrSetAsync<TValue>(
        string key,
        Func<CancellationToken, Task<TValue>> factory,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

        return cache.GetOrSetAsync<TValue>(
            AddPrefix(key),
            factory,
            options,
            tags: AddPrefix(tags),
            cancellationToken);
    }

    //

    public void Set<TValue>(
        string key,
        TValue value,
        TimeSpan duration,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

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
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate(duration);

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
        var options = DefaultEntryOptions.Duplicate();

        cache.Remove(
            AddPrefix(key),
            options,
            cancellationToken);
    }

    //

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var options = DefaultEntryOptions.Duplicate();

        return cache.RemoveAsync(
            AddPrefix(key),
            options,
            cancellationToken);
    }
    
    //

    public void RemoveByTag(string tag, CancellationToken cancellationToken = default)
    {
        cache.RemoveByTag(
            AddPrefix(tag),
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }
    
    //

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        return cache.RemoveByTagAsync(
            AddPrefix(tag),
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }

    //

    public void RemoveByTag(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags, nameof(tags));

        cache.RemoveByTag(
            AddPrefix(tags)!,
            null, // SEB:NOTE do not pass entry-options here, they are meant for cache entries, not for tag entries
            cancellationToken);
    }

    //

    public ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags, nameof(tags));

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
