using System;
using System.Runtime.Caching;
using Quartz;

namespace Odin.Services.JobManagement;
#nullable enable

public interface IJobMemoryCache
{
    bool TryGet(string key, out object? value);
    bool TryGet<T>(string key, out T? value);
    bool TryGet<T>(IJobExecutionContext jobExecutionContext, out T? value);
    bool TryGet<T>(JobKey jobKey, out T? value);
    void Insert(JobKey jobKey, object value, TimeSpan lifespan);
    void Insert(string key, object value, TimeSpan lifespan);
    object Remove(JobKey jobKey);
    object Remove(string key);
    bool Contains(string key);
    bool Contains(JobKey key);
}

//

public class JobMemoryCache : IJobMemoryCache
{
    private readonly MemoryCache _cache = new("JobMemoryCache");

    //

    public bool TryGet(string key, out object? value)
    {
        value = _cache.Get(key);
        return value != null;
    }

    //

    public bool TryGet<T>(IJobExecutionContext jobExecutionContext, out T? value)
    {
        return TryGet(jobExecutionContext.JobDetail.Key, out value);
    }

    //

    public bool TryGet<T>(JobKey jobKey, out T? value)
    {
        return TryGet(jobKey.ToString(), out value);
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
                throw new InvalidCastException($"The item with key '{key}' is not of type {typeof(T).Name}.");
        }
    }

    //

    public void Insert(JobKey jobKey, object value, TimeSpan lifespan)
    {
        Insert(jobKey.ToString(), value, lifespan);
    }

    //

    public void Insert(string key, object value, TimeSpan lifespan)
    {
        var policy = new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.Add(lifespan) };
        _cache.Set(new CacheItem(key, value), policy);
    }

    //

    public object Remove(JobKey jobKey)
    {
        return _cache.Remove(jobKey.ToString());
    }

    //

    public object Remove(string key)
    {
        return _cache.Remove(key);
    }

    //

    public bool Contains(string key)
    {
        return _cache.Contains(key);
    }

    //

    public bool Contains(JobKey jobKey)
    {
        return _cache.Contains(jobKey.ToString());
    }
}
