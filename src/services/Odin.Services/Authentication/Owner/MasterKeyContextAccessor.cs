using System;
using Odin.Core.Cache;
using Odin.Services.Base;

namespace Odin.Services.Authentication.Owner;

public class MasterKeyContextAccessor
{
    private const string CacheKey = "mk-context";
    private readonly TimeSpan _holdTime = TimeSpan.FromSeconds(100);
    private readonly GenericMemoryCache _cache = new("mk-context");

    public void SetContext(OdinContext context)
    {
        if (!_cache.TryGet<OdinContext>(CacheKey, out _))
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
}