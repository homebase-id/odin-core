using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Cache;

namespace Odin.Services.Authorization.Capi;


public interface ICapiCallbackSession
{
    const string SessionHttpHeaderName = "X-CAPI-Session-Id";
    Task<Guid> EstablishSessionAsync(string remoteDomain, TimeSpan sessionLifetime);
    Task<bool> ValidateSessionAsync(string remoteDomain, Guid sessionId);
}

//

public class CapiCallbackSession(ITenantLevel2Cache<CapiCallbackSession> cache) : ICapiCallbackSession
{
    public async Task<Guid> EstablishSessionAsync(string remoteDomain, TimeSpan sessionLifetime)
    {
        var cacheKey = GetCacheKey(remoteDomain);
        var session = await cache.GetOrSetAsync(cacheKey, _ => Task.FromResult(Guid.NewGuid()), sessionLifetime);
        return session;
    }

    //

    public async Task<bool> ValidateSessionAsync(string remoteDomain, Guid sessionId)
    {
        var cacheKey = GetCacheKey(remoteDomain);
        var sessionLookUp = await cache.TryGetAsync<Guid>(cacheKey);
        return sessionLookUp.HasValue && sessionLookUp.Value == sessionId;
    }

    //

    private static string GetCacheKey(string remoteDomain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteDomain);
        return "capi-session:" + remoteDomain;
    }
}
