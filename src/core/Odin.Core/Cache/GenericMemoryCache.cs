using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Threading;

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
    void Set(string key, object? value, DateTimeOffset absoluteExpiration);
    void Set(byte[] key, object? value, TimeSpan lifespan);
    void Set(byte[] key, object? value, DateTimeOffset absoluteExpiration);
    T? GetOrCreate<T>(string key, Func<T?> factory, DateTimeOffset absoluteExpiration);
    T? GetOrCreate<T>(string key, Func<T?> factory, TimeSpan lifespan);
    T? GetOrCreate<T>(byte[] key, Func<T?> factory, DateTimeOffset absoluteExpiration);
    T? GetOrCreate<T>(byte[] key, Func<T?> factory, TimeSpan lifespan);
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, DateTimeOffset absoluteExpiration);
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan lifespan);
    Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, DateTimeOffset absoluteExpiration);
    Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, TimeSpan lifespan);
    object? Remove(string key);
    object? Remove(byte[] key);
    bool Contains(string key);
    bool Contains(byte[] key);
    string GenerateKey(string prefix, params string[] values);
    string GenerateKey(string prefix, params byte[][] values);
}

//

public sealed class GenericMemoryCache : IGenericMemoryCache, IDisposable
{
    private static readonly object NullValue = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly KeyedAsyncLock _factoryLock = new();

    public void Dispose()
    {
        _cache.Dispose();
    }

    //

    public void Clear()
    {
        _cache.Clear();
    }

    //

    public bool TryGet(string key, out object? value)
    {
        if (_cache.TryGetValue(key, out var result))
        {
            if (ReferenceEquals(result, NullValue))
            {
                value = default;
                return true;
            }

            value = result;
            return true;
        }

        value = default;
        return false;
    }

    //

    public bool TryGet(byte[] key, out object? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
    }

    //

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out var result))
        {
            if (ReferenceEquals(result, NullValue))
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

        value = default;
        return false;
    }

    //

    public bool TryGet<T>(byte[] key, out T? value)
    {
        return TryGet(Convert.ToBase64String(key), out value);
    }

    //

    public void Set(string key, object? value, TimeSpan lifespan)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifespan
        };
        _cache.Set(key, value ?? NullValue, options);
    }

    //

    public void Set(string key, object? value, DateTimeOffset absoluteExpiration)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration
        };
        _cache.Set(key, value ?? NullValue, options);
    }

    //

    public void Set(byte[] key, object? value, TimeSpan lifespan)
    {
        Set(Convert.ToBase64String(key), value, lifespan);
    }

    //

    public void Set(byte[] key, object? value, DateTimeOffset absoluteExpiration)
    {
        Set(Convert.ToBase64String(key), value, absoluteExpiration);
    }

    //

    public T? GetOrCreate<T>(string key, Func<T?> factory, DateTimeOffset absoluteExpiration)
    {
        if (TryGet<T?>(key, out var existingValue))
        {
            return existingValue;
        }

        using (_factoryLock.Lock(key))
        {
            // Double-check if the value was added while waiting for the lock
            if (TryGet<T?>(key, out existingValue))
            {
                return existingValue;
            }

            // Execute the factory function and store the result
            var value = factory();
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration
            };
            _cache.Set(key, value ?? NullValue, options);

            return value;
        }
    }
    //

    public T? GetOrCreate<T>(string key, Func<T?> factory, TimeSpan lifespan)
    {
        return GetOrCreate(key, factory, DateTimeOffset.UtcNow.Add(lifespan));
    }

    //

    public T? GetOrCreate<T>(byte[] key, Func<T?> factory, DateTimeOffset absoluteExpiration)
    {
        return GetOrCreate(Convert.ToBase64String(key), factory, absoluteExpiration);
    }

    //

    public T? GetOrCreate<T>(byte[] key, Func<T?> factory, TimeSpan lifespan)
    {
        return GetOrCreate(Convert.ToBase64String(key), factory, lifespan);
    }

    //

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, DateTimeOffset absoluteExpiration)
    {
        if (TryGet<T?>(key, out var existingValue))
        {
            return existingValue;
        }

        using (await _factoryLock.LockAsync(key))
        {
            // Double-check if the value was added while waiting for the lock
            if (TryGet<T?>(key, out existingValue))
            {
                return existingValue;
            }

            // Execute the factory function and store the result
            var value = await factory().ConfigureAwait(false);
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration
            };
            _cache.Set(key, value ?? NullValue, options);

            return value;
        }
    }

    //

    public Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, TimeSpan lifespan)
    {
        return GetOrCreateAsync(key, factory, DateTimeOffset.UtcNow.Add(lifespan));
    }

    //

    public Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, DateTimeOffset absoluteExpiration)
    {
        return GetOrCreateAsync(Convert.ToBase64String(key), factory, absoluteExpiration);
    }

    //

    public Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, TimeSpan lifespan)
    {
        return GetOrCreateAsync(Convert.ToBase64String(key), factory, DateTimeOffset.UtcNow.Add(lifespan));
    }

    //

    public object? Remove(string key)
    {
        _cache.TryGetValue(key, out var value);
        _cache.Remove(key);
        return ReferenceEquals(value, NullValue) ? null : value;
    }

    //

    public object? Remove(byte[] key)
    {
        return Remove(Convert.ToBase64String(key));
    }

    //

    public bool Contains(string key)
    {
        return _cache.TryGetValue(key, out _);
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
}
