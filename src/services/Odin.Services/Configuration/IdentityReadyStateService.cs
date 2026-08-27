using System;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Configuration;

/// <summary>
/// Answers whether the identity has completed its initial configuration.
/// <para>
/// This is asked on every /api request by <c>IdentityReadyStateMiddleware</c>, so it deliberately
/// depends only on the (cached) key-value store rather than living on <see cref="TenantConfigService"/>,
/// whose dependency graph is expensive to build per request. <see cref="TenantConfigService"/>
/// delegates here so there is a single implementation.
/// </para>
/// </summary>
public class IdentityReadyStateService(IdentityDatabase identityDatabase)
{
    private static readonly SingleKeyValueStorage ConfigStorage =
        TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(TenantConfigService.ConfigContextKey));

    public async Task<bool> IsIdentityServerConfiguredAsync()
    {
        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = await ConfigStorage.GetAsync<FirstRunInfo>(identityDatabase.KeyValueCached, FirstRunInfo.Key);
        return firstRunInfo != null;
    }

    public async Task<UnixTimeUtc?> GetFirstRunDateAsync()
    {
        //ok for anonymous to query this as long as we're only returning a bool
        var firstRunInfo = await ConfigStorage.GetAsync<FirstRunInfo>(identityDatabase.KeyValueCached, FirstRunInfo.Key);
        return firstRunInfo?.FirstRunDate;
    }
}
