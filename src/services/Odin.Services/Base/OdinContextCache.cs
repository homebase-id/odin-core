using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base;

public class OdinContextCache
{
    private readonly int _ttlSeconds;
    private readonly IAppCache _dotYouContextCache;
    private readonly CancellationTokenSource _expiryTokenSource = new();

    public OdinContextCache(int ttlSeconds = 60)
    {
        this._ttlSeconds = ttlSeconds;
        _dotYouContextCache = new CachingService();
    }

    public async Task<IOdinContext> GetOrAddContextAsync(ClientAuthenticationToken token, Func<Task<IOdinContext>> dotYouContextFactory)
    {
        var key = token.AsKey().ToString().ToLower();
        var policy = new MemoryCacheEntryOptions()
        {
            SlidingExpiration = TimeSpan.FromSeconds(_ttlSeconds)
        };

        policy.AddExpirationToken(new CancellationChangeToken(_expiryTokenSource.Token));
        var result = await _dotYouContextCache.GetOrAddAsync<IOdinContext>(key, dotYouContextFactory, policy);

        //TODO: Need some locking on _identitiesRequiringReset
        // var rebuildContext = _identitiesRequiringReset.Contains(result.Caller.OdinId);
        // if (rebuildContext)
        // {
        //     _dotYouContextCache.Remove(key);
        //     result = await _dotYouContextCache.GetOrAddAsync<DotYouContext>(key, dotYouContextFactory, policy);
        //     _identitiesRequiringReset.Remove(result.Caller.OdinId);
        //     
        // }

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

    public void EnqueueIdentityForReset(OdinId identity)
    {
        //TODO: need to find a way to do this per identity instead all items
        //todo: locking
        // if (!_identitiesRequiringReset.Contains(identity))
        // {
        //     _identitiesRequiringReset.Add(identity);
        // }

        this.Reset();
    }
}

public class SharedOdinContextCache<TRegisteredService>(int ttlSeconds = 60) : OdinContextCache(ttlSeconds);

