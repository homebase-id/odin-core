using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public class OdinContextCache
{
    private readonly int _ttlSeconds;
    private readonly IAppCache _dotYouContextCache;
    private readonly CancellationTokenSource _expiryTokenSource = new();
    private readonly List<OdinId> _identitiesRequiringReset;

    public OdinContextCache(int ttlSeconds = 60)
    {
        _identitiesRequiringReset = new List<OdinId>();
        this._ttlSeconds = ttlSeconds;
        _dotYouContextCache = new CachingService();
    }

    public async Task<OdinContext> GetOrAddContext(ClientAuthenticationToken token, Func<Task<OdinContext>> dotYouContextFactory)
    {
        var key = token.AsKey().ToString().ToLower();
        var policy = new MemoryCacheEntryOptions()
        {
            SlidingExpiration = TimeSpan.FromSeconds(_ttlSeconds)
        };

        policy.AddExpirationToken(new CancellationChangeToken(_expiryTokenSource.Token));
        var result = await _dotYouContextCache.GetOrAddAsync<OdinContext>(key, dotYouContextFactory, policy);

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
        throw new NotImplementedException();
        //todo: locking
        // if (!_identitiesRequiringReset.Contains(identity))
        // {
        //     _identitiesRequiringReset.Add(identity);
        // }
    }
}