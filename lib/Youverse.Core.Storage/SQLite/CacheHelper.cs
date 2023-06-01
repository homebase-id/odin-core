﻿using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Caching;

namespace Youverse.Core.Storage.SQLite
{
    public class CacheHelper
    {
        private readonly MemoryCache _cache;
        private readonly CacheItemPolicy _defaultPolicy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10) };
        private static Object _cacheNull = new Object();

        public CacheHelper(string name)
        {
            _cache = new MemoryCache(name, new NameValueCollection
            {
                { "cacheMemoryLimitMegabytes", "1" },
                { "physicalMemoryLimitPercentage", "1" },
                { "pollingInterval", "00:02:00" }
            });
        }

        public void AddOrUpdate(string table, Guid key, object value)
        {
            AddOrUpdate(table, key.ToString(), value);
        }

        public void AddOrUpdate(string table, string key, object value)
        {
            if (value == null)
                _cache.Set(table+key, _cacheNull, _defaultPolicy);
            else
                _cache.Set(table+key, value, _defaultPolicy);
        }

        public (bool, object) Get(string table, string key)
        {
            var r = _cache.Get(table+key);

            if (r == null)
                return (false, null);

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
            _cache.Remove(table + key);
        }

        public void Remove(string table, Guid key)
        {
            _cache.Remove(table + key.ToString());
        }
    }
}
