using System;
using System.Collections.Specialized;
using System.Runtime.Caching;

namespace Odin.Core.Storage
{
    /*
     *  If we want to cache more than atomic records then we can add a List<string> facets as
     *  parameter  and keep track of them separately in a dictionary.
     */
    public class CacheHelper
    {
        private readonly string _name;
        private MemoryCache _cache;
        private readonly CacheItemPolicy _defaultPolicy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10) };
        private static Object _cacheNull = new Object();
        private int _cacheGets = 0;
        private int _cacheHits = 0;
        private int _cacheSets = 0;
        private int _cacheRemove = 0;

        public int GetCacheGets() { return _cacheGets; }
        public int GetCacheHits() { return _cacheHits; }
        public int GetCacheSets() { return _cacheSets; }
        public int GetCacheRemove() { return _cacheRemove; }

        public void ClearCache() { Initialize(); }


        private void Initialize()
        {
            _cache = new MemoryCache(_name, new NameValueCollection
            {
                { "cacheMemoryLimitMegabytes", "1" },
                { "physicalMemoryLimitPercentage", "1" },
                { "pollingInterval", "00:02:00" }
            });
            _cacheGets = 0;
            _cacheHits = 0;
            _cacheSets = 0;
            _cacheRemove = 0;
        }

        public CacheHelper(string name)
        {
            _name = name;
            Initialize();
        }

        public void AddOrUpdate(string table, Guid key, object value)
        {
            AddOrUpdate(table, key.ToString(), value);
        }

        public void AddOrUpdate(string table, string key, object value)
        {
            _cacheSets++;
            if (value == null)
                _cache.Set(table+key, _cacheNull, _defaultPolicy);
            else
                _cache.Set(table+key, value, _defaultPolicy);
        }

        public (bool, object) Get(string table, string key)
        {
            _cacheGets++;
            var r = _cache.Get(table+key);

            if (r == null)
                return (false, null);

            _cacheHits++;
            if (r == _cacheNull)
                return (true, null);
            else
                return (true, r);
        }

        public (bool, object) Get(string table, Guid key)
        {
            return Get(table, key.ToString());
        }

        public void Remove(string table, string key)
        {
            _cacheRemove++;
            _cache.Remove(table + key);
        }

        public void Remove(string table, Guid key)
        {
            _cacheRemove++;
            _cache.Remove(table + key.ToString());
        }
    }
}
