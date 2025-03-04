using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base;

public class OdinContextCache(ITenantLevel2Cache<OdinContextCache> cache)
{
    private readonly List<string> _tags = [Guid.NewGuid().ToString()];

    //

    public async Task<IOdinContext> GetOrAddContextAsync(
        ClientAuthenticationToken token,
        Func<Task<IOdinContext>> dotYouContextFactory,
        int ttlSeconds = 60)
    {
        var key = token.AsKey().ToString().ToLower();

        var result = await cache.GetOrSetAsync<IOdinContext>(
            key,
            _ => dotYouContextFactory(),
            TimeSpan.FromSeconds(ttlSeconds),
            _tags
        );

        return result;
    }

    //

    public async Task ResetAsync()
    {
        await cache.RemoveByTagAsync(_tags);
    }
}


