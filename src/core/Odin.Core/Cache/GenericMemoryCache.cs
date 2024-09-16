using System;
using System.Linq;
using System.Runtime.Caching;
using System.Text;

namespace Odin.Core.Cache;

#nullable enable

public interface IGenericMemoryCache
{
    void Clear();
    bool TryGet(string key, out object? value);
    bool TryGet(byte[] key, out object? value);
    bool TryGet<T>(string key, out T? value);
    bool TryGet<T>(byte[] key, out T? value);
    void Set(string key, object? value, TimeSpan lifespan);
    void Set(byte[] key, object? value, TimeSpan lifespan);
    object? Remove(string key);
    object? Remove(byte[] key);
    bool Contains(string key);
    bool Contains(byte[] key);
    string GenerateKey(string prefix, params string[] values);
    string GenerateKey(string prefix, params byte[][] values);
}

//

public class GenericMemoryCache(string name = "generic-memory-cache") : IGenericMemoryCache, IDisposable
{
    private static readonly object NullValue = new ();
    private readonly object _mutex = new(); // We need locking because user can call Clear() at any time
    private MemoryCache _cache = new(name);

    //

    public void Dispose()
    {
        lock (_mutex)
        {
            _cache.Dispose();
        }
    }

    //

    public void Clear()
    {
        lock (_mutex)
        {
            var name = _cache.Name;
            _cache.Dispose();
            _cache = new MemoryCache(name);
        }
    }

    //

    public bool TryGet(string key, out object? value)
    {
        object? result;
        lock (_mutex)
        {
            result = _cache.Get(key);
        }

        if (result == null)
        {
            value = default;
            return false;
        }

        if (result == NullValue)
        {
            value = default;
            return true;
        }

        value = result;
        return true;
    }

    //

    public bool TryGet(byte[] key, out object? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
    }

    //

    public bool TryGet<T>(string key, out T? value)
    {
        object? result;
        lock (_mutex)
        {
            result = _cache.Get(key);
        }

        if (result == null)
        {
            value = default;
            return false;
        }

        if (result == NullValue)
        {
            value = default;
            return true;
        }

        if (result is T actual)
        {
            value = actual;
            return true;
        }

        throw new InvalidCastException($"The item with key '{key}' cannot be cast to type {typeof(T).Name}.");
    }

    //

    public bool TryGet<T>(byte[] key, out T? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
    }

    //

    public void Set(string key, object? value, TimeSpan lifespan)
    {
        var policy = new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(lifespan) };
        lock (_mutex)
        {
            _cache.Set(new CacheItem(key, value ?? NullValue), policy);
        }
    }

    //

    public void Set(byte[] key, object? value, TimeSpan lifespan)
    {
        Set(Convert.ToBase64String(key), value, lifespan);
    }

    //

    public object? Remove(string key)
    {
        lock (_mutex)
        {
            return _cache.Remove(key);
        }
    }

    //

    public object? Remove(byte[] key)
    {
        return Remove(Convert.ToBase64String(key));
    }

    //

    public bool Contains(string key)
    {
        lock (_mutex)
        {
            return _cache.Contains(key);
        }
    }

    //

    public bool Contains(byte[] key)
    {
        return Contains(Convert.ToBase64String(key));
    }

    //

    public string GenerateKey(string prefix, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix, nameof(prefix));

        if (values.Length == 0)
        {
            return prefix;
        }

        // SEB:NOTE random guestimate on the sweet spot for when to use string.Join vs StringBuilder
        if (values.Length < 5)
        {
            return $"{prefix}:{string.Join(":", values)}";
        }

        var capacity = prefix.Length + 1 + values.Sum(v => v.Length + 1);
        var sb = new StringBuilder(capacity);
        sb.Append(prefix);

        foreach (var value in values)
        {
            sb.Append(':').Append(value);
        }

        return sb.ToString();
    }

    //

    public string GenerateKey(string prefix, params byte[][] values)
    {
        var strings = new string[values.Length];

        for (var idx = 0; idx < values.Length; idx++)
        {
            strings[idx] = Convert.ToBase64String(values[idx]);
        }

        return GenerateKey(prefix, strings);
    }

    //

}