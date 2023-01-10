using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public struct CacheItem
{
    public DotYouContext DotYouContext { get; set; }

    public UnixTimeUtc Created { get; set; }
}

public class DotYouContextCache
{
    private readonly int _ttlSeconds;

    //TODO: maybe make this a sliding cache?
    private readonly ConcurrentDictionary<Guid, CacheItem> _contextCache = new();
    private readonly object _readLock = new();

    public DotYouContextCache(int ttlSeconds = 60)
    {
        this._ttlSeconds = ttlSeconds;
    }
    
    public bool TryGetContext(ClientAuthenticationToken token, out DotYouContext context)
    {
        CacheItem item;
        if (!_contextCache.TryGetValue(token.AsKey(), out item))
        {
            context = null;
            return false;
        }

        var expires = item.Created.AddSeconds(_ttlSeconds);
        if (UnixTimeUtc.Now() > expires)
        {
            context = null;
            return false;
        }

        context = item.DotYouContext;
        return true;
    }

    public void CacheContext(ClientAuthenticationToken token, DotYouContext dotYouContext)
    {
        _contextCache.TryAdd(token.AsKey(), new CacheItem()
        {
            DotYouContext = dotYouContext,
            Created = UnixTimeUtc.Now()
        });
    }

    public void Purge()
    {
        _contextCache.Clear();
    }
}