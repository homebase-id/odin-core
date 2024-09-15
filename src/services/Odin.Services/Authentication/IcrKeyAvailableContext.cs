using System;
using System.Linq;
using System.Threading;
using Odin.Core.Cache;
using Odin.Services.Base;

namespace Odin.Services.Authentication;

/// <summary>
/// Information when the ICR key is available for background processing
/// </summary>
public class IcrKeyAvailableContext
{
    private const string CacheKey = "icr-context";
    private readonly TimeSpan _holdTime = TimeSpan.FromSeconds(100);
    private readonly GenericMemoryCache _cache = new("icr-context-cache");
    private readonly CancellationTokenSource _expiryTokenSource = new();

    public void SetContext(OdinContext context)
    {
        // if (!_cache.TryGet<OdinContext>(CacheKey, out _))
        {
            _cache.Set(CacheKey, (OdinContext)context.Clone(), _holdTime);
        }
    }

    public OdinContext GetContext()
    {
        if (_cache.TryGet(CacheKey, out OdinContext ctx))
        {
            return ctx;
        }

        return null;
    }

    public void Reset()
    {
        //from: https://github.com/alastairtree/LazyCache/wiki/API-documentation-(v-2.x)#empty-the-entire-cache
        _expiryTokenSource.Cancel();
    }
}