using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base;

#nullable enable

public class OdinContextCache(
    ITenantLevel1Cache<OdinContextCache> level1Cache,
    ITenantLevel2Cache<OdinContextCache> level2Cache)
{
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(1);
    private readonly List<string> _cacheTags = [Guid.NewGuid().ToString()];

    //

    public async Task<IOdinContext?> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext?>> dotYouContextFactory,
        TimeSpan? duration = null)
    {
        var key = token.AsKey().ToString().ToLower();

        var result = await level1Cache.GetOrSetAsync(
            key,
            _ => dotYouContextFactory(),
            duration ?? DefaultDuration,
            _cacheTags
        );

        return result;
    }

    //

    public async Task ResetAsync()
    {
        await level1Cache.RemoveByTagAsync(_cacheTags);
        await level2Cache.RemoveByTagAsync(_cacheTags);
    }
}


