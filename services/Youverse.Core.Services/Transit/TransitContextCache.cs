using System;
using System.Collections.Concurrent;
using System.Threading;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Transit;

public class TransitContextCache
{
    private readonly ConcurrentDictionary<Guid, Lazy<CacheItem>> _cache = new();

    public DotYouContext GetOrAdd(ClientAuthenticationToken token,
        Func<ClientAuthenticationToken, DotYouContext> dotYouContextFactory)
    {
        var item = _cache.GetOrAdd(token.AsKey(), key =>
        {
            return new Lazy<CacheItem>(() =>
            {
                var context = dotYouContextFactory(token);
                return new CacheItem()
                {
                    Created = UnixTimeUtc.Now(),
                    DotYouContext = context
                };
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        });
        
        return item.Value.DotYouContext;
    }
}