using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base;

#nullable enable

public class OdinContextCache(
    CacheConfiguration config,
    ITenantLevel1Cache<OdinContextCache> level1Cache,
    ITenantLevel2Cache<OdinContextCache> level2Cache)
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(5);
    private readonly List<string> _cacheTags = [Guid.NewGuid().ToString()];

    //

    public async Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext?>> dotYouContextFactory,
        TimeSpan? expiration = null)
    {
        var duration = expiration ?? DefaultDuration;
        if (duration < TimeSpan.FromSeconds(1))
        {
            throw new OdinSystemException("Cache duration must be at least 1 second.");
        }

        var key = token.AsKey().ToString().ToLower();

        var isValid = true;
        if (config.Level2CacheType != Level2CacheType.None)
        {
            isValid = (await level2Cache.TryGetAsync<bool>(key)).GetValueOrDefault();
        }

        if (isValid)
        {
            var value = await level1Cache.TryGetAsync<IOdinContext?>(key);
            if (value.HasValue)
            {
                return value.Value;
            }
        }

        var odinContext = await dotYouContextFactory();
        await level1Cache.SetAsync(key, odinContext, duration, _cacheTags);

        if (config.Level2CacheType != Level2CacheType.None)
        {
            await level2Cache.SetAsync(key, true, duration);
        }

        return odinContext;
    }

    //

    public async Task ResetAsync()
    {
        if (config.Level2CacheType != Level2CacheType.None)
        {
            await level2Cache.RemoveByTagAsync(_cacheTags);
        }
        await level1Cache.RemoveByTagAsync(_cacheTags);
    }
}


