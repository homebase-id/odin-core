using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public class DotYouContextCache
{
    private readonly int _ttlSeconds;
    private IAppCache _dotYouContextCache;

    public DotYouContextCache(int ttlSeconds = 60)
    {
        this._ttlSeconds = ttlSeconds;
        _dotYouContextCache = new CachingService();
    }

    public async Task<DotYouContext> GetOrAddContext(ClientAuthenticationToken token, Func<Task<DotYouContext>> dotYouContextFactory)
    {
        var key = token.AsKey().ToString().ToLower();
        var result = await _dotYouContextCache.GetOrAddAsync<DotYouContext>(key, dotYouContextFactory);
        return result;
    }

    /// <summary>
    /// Fully empties the Cache
    /// </summary>
    public void Reset()
    {
        //from: https://github.com/alastairtree/LazyCache/wiki/API-documentation-(v-2.x)#empty-the-entire-cache
        _dotYouContextCache?.CacheProvider?.Dispose();
        var provider = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()));
        _dotYouContextCache = new CachingService(provider);
    }
}