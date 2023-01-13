using System;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public interface ICacheDotYouContext
{
    /// <summary>
    /// Adds or updates the DotYouContext to cache
    /// </summary>
    /// <param name="token"></param>
    /// <param name="dotYouContext"></param>
    void GetOrAddContext(ClientAuthenticationToken token, DotYouContext dotYouContext);

    /// <summary>
    /// Empties and resets the cache
    /// </summary>
    void Reset();
}