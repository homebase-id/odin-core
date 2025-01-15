using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Odin.Core.Exceptions;
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
    void Set(string key, object? value, MemoryCacheEntryOptions options);
    void Set(byte[] key, object? value, MemoryCacheEntryOptions options);
    T? GetOrCreate<T>(string key, Func<T?> factory, MemoryCacheEntryOptions options);
    T? GetOrCreate<T>(byte[] key, Func<T?> factory, MemoryCacheEntryOptions options);
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, MemoryCacheEntryOptions options);
    Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, MemoryCacheEntryOptions options);
    object? Remove(string key);
    object? Remove(byte[] key);
    bool Contains(string key);
    bool Contains(byte[] key);
    string GenerateKey(string prefix, params string[] values);
    string GenerateKey(string prefix, params byte[][] values);
}

public interface IGenericMemoryCache<TRegistration> : IGenericMemoryCache;

//

public class GenericMemoryCache : IGenericMemoryCache, IDisposable
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

    public void Set(string key, object? value, MemoryCacheEntryOptions options)
    {
        Check(options);
        _cache.Set(key, value ?? NullValue, options);
    }

    //

    public void Set(byte[] key, object? value, MemoryCacheEntryOptions options)
    {
        Set(Convert.ToBase64String(key), value, options);
    }

    //

    public T? GetOrCreate<T>(string key, Func<T?> factory, MemoryCacheEntryOptions options)
    {
        Check(options);

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
            _cache.Set(key, value ?? NullValue, options);

            return value;
        }
    }
    //

    public T? GetOrCreate<T>(byte[] key, Func<T?> factory, MemoryCacheEntryOptions options)
    {
        return GetOrCreate(Convert.ToBase64String(key), factory, options);
    }

    //

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T?>> factory, MemoryCacheEntryOptions options)
    {
        Check(options);

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
            _cache.Set(key, value ?? NullValue, options);

            return value;
        }
    }

    //

    public Task<T?> GetOrCreateAsync<T>(byte[] key, Func<Task<T?>> factory, MemoryCacheEntryOptions options)
    {
        return GetOrCreateAsync(Convert.ToBase64String(key), factory, options);
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

    //

    private static void Check(MemoryCacheEntryOptions options)
    {
        var badOptions =
            options.AbsoluteExpiration == null &&
            options.AbsoluteExpirationRelativeToNow == null &&
            options.SlidingExpiration == null;
        if (badOptions)
        {
            throw new OdinSystemException("MemoryCacheEntryOptions: missing expiration");
        }
    }
}

public class GenericMemoryCache<TRegistration> : GenericMemoryCache, IGenericMemoryCache<TRegistration>;

//

public static class Expiration
{
    public static MemoryCacheEntryOptions Absolute(DateTimeOffset absoluteExpiration)
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration
        };
    }

    public static MemoryCacheEntryOptions Relative(TimeSpan absoluteExpirationRelativeToNow)
    {
        return new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow
        };
    }

    public static MemoryCacheEntryOptions Sliding(TimeSpan slidingExpiration)
    {
        return new MemoryCacheEntryOptions
        {
            SlidingExpiration = slidingExpiration
        };
    }
}
