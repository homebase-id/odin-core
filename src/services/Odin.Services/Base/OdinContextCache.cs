using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;
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
    private readonly AsyncReaderWriterLock _level2Lock = new();
    private readonly List<string> _cacheTags = [Guid.NewGuid().ToString()];

    //

    public async Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext?>> dotYouContextFactory,
        TimeSpan? expiration = null)
    {
        var duration = expiration ?? config.DefaultDuration;
        if (duration < TimeSpan.FromSeconds(1))
        {
            throw new OdinSystemException("Cache duration must be at least 1 second.");
        }

        var key = token.AsKey().ToString().ToLower();

        //
        // NOTE: we use an r/w lock to ensure that multiple interleaving threads
        // won't race deleting and (re)creating the same cache entry whenever the L2 cache entry is missing.
        // This will introduce a small bottleneck when different cache keys are being accessed concurrently,
        // and have to pass through the same lock, but trying to optimize this per-key quickly becomes a mess.
        //

        if (config.Level2CacheType != Level2CacheType.None)
        {
            var level2Hit = await level2Cache.TryGetAsync<bool>(key);
            if (!level2Hit.HasValue)
            {
                using (await _level2Lock.WriterLockAsync())
                {
                    level2Hit = await level2Cache.TryGetAsync<bool>(key);
                    if (!level2Hit.HasValue)
                    {
                        await level1Cache.RemoveAsync(key);
                        await level2Cache.SetAsync(key, true, duration);
                    }
                }
            }
        }

        using (await _level2Lock.ReaderLockAsync())
        {
            var result = await level1Cache.GetOrSetAsync(
                key,
                _ => dotYouContextFactory(),
                duration,
                _cacheTags
            );

            return result;
        }
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


