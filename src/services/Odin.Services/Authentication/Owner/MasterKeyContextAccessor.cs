using System;
using Odin.Core.Cache;
using Odin.Services.Base;

namespace Odin.Services.Authentication.Owner;

public class MasterKeyContextAccessor
{
    private const string CacheKey = "mk-context";
    private readonly TimeSpan _holdTime = TimeSpan.FromSeconds(100);
    private readonly GenericMemoryCache _cache = new("mk-context");

    public void SetContext(IOdinContext context)
    {
        if (!_cache.TryGet(CacheKey, out _))
        {
            _cache.Set(CacheKey, context.Clone(), _holdTime);
        }
    }

    public IOdinContext GetContext()
    {
        if (_cache.TryGet(CacheKey, out IOdinContext ctx))
        {
            return ctx;
        }

        return null;
    }
}