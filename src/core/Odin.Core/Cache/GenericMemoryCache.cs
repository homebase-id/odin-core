using System;
using System.Runtime.Caching;

namespace Odin.Core.Cache;

#nullable enable

public interface IGenericMemoryCache
{
    bool TryGet(string key, out object? value);
    bool TryGet<T>(string key, out T? value);
    void Set(string key, object value, TimeSpan lifespan);
    object? Remove(string key);
    bool Contains(string key);
}

public class GenericMemoryCache : IGenericMemoryCache
{
    private readonly MemoryCache _cache = new("generic-memory-cache");

    public bool TryGet(string key, out object? value)
    {
        value = _cache.Get(key);
        return value != null;
    }

    //

    public bool TryGet<T>(string key, out T? value)
    {
        var result = _cache.Get(key);

        switch (result)
        {
            case null:
                value = default;
                return false;
            case T typedValue:
                value = typedValue;
                return true;
            default:
                throw new InvalidCastException($"The item with key '{key}' cannot be cast to type {typeof(T).Name}.");
        }
    }

    //

    public void Set(string key, object value, TimeSpan lifespan)
    {
        var policy = new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(lifespan) };
        _cache.Set(new CacheItem(key, value), policy);
    }

    //

    public object? Remove(string key)
    {
        return _cache.Remove(key);
    }

    //

    public bool Contains(string key)
    {
        return _cache.Contains(key);
    }
}