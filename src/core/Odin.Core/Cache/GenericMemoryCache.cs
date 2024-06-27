using System;
using System.Runtime.Caching;

namespace Odin.Core.Cache;

#nullable enable

public interface IGenericMemoryCache
{
    bool TryGet(string key, out object? value);
    bool TryGet(byte[] key, out object? value);
    bool TryGet<T>(string key, out T? value);
    bool TryGet<T>(byte[] key, out T? value);
    void Set(string key, object value, TimeSpan lifespan);
    void Set(byte[] key, object value, TimeSpan lifespan);
    object? Remove(string key);
    object? Remove(byte[] key);
    bool Contains(string key);
    bool Contains(byte[] key);
}

public class GenericMemoryCache(string name = "generic-memory-cache") : IGenericMemoryCache
{
    private readonly MemoryCache _cache = new(name);

    //

    public bool TryGet(string key, out object? value)
    {
        value = _cache.Get(key);
        return value != null;
    }

    //

    public bool TryGet(byte[] key, out object? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
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

    public bool TryGet<T>(byte[] key, out T? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
    }

    //

    public void Set(string key, object value, TimeSpan lifespan)
    {
        var policy = new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(lifespan) };
        _cache.Set(new CacheItem(key, value), policy);
    }

    //

    public void Set(byte[] key, object value, TimeSpan lifespan)
    {
        Set(Convert.ToBase64String(key), value, lifespan);
    }

    //

    public object? Remove(string key)
    {
        return _cache.Remove(key);
    }

    //

    public object? Remove(byte[] key)
    {
        return Remove(Convert.ToBase64String(key));
    }

    //

    public bool Contains(string key)
    {
        return _cache.Contains(key);
    }

    //

    public bool Contains(byte[] key)
    {
        return Contains(Convert.ToBase64String(key));
    }

}