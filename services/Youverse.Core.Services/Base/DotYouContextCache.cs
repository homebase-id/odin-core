using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public class DotYouContextCache
{
    private readonly int _ttlSeconds;
    private IAppCache _dotYouContextCache;
    private CancellationTokenSource _expiryTokenSource = new ();

    public DotYouContextCache(int ttlSeconds = 60)
    {
        this._ttlSeconds = ttlSeconds;
        _dotYouContextCache = new CachingService();
    }

    public async Task<DotYouContext> GetOrAddContext(ClientAuthenticationToken token, Func<Task<DotYouContext>> dotYouContextFactory)
    {
        var key = token.AsKey().ToString().ToLower();
        var policy = new MemoryCacheEntryOptions()
        {
            SlidingExpiration = TimeSpan.FromSeconds(_ttlSeconds)
        };
        
        policy.AddExpirationToken(new CancellationChangeToken(_expiryTokenSource.Token));
        var result = await _dotYouContextCache.GetOrAddAsync<DotYouContext>(key, dotYouContextFactory, policy);
        return result;
    }

    /// <summary>
    /// Fully empties the Cache
    /// </summary>
    public void Reset()
    {
        //from: https://github.com/alastairtree/LazyCache/wiki/API-documentation-(v-2.x)#empty-the-entire-cache
        _expiryTokenSource.Cancel();
    }
}