using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base;

public interface ICacheDotYouContext
{
    /// <summary>
    /// Returns a bool indicating if DotYouContext is cached.  Value comes from the out param 
    /// </summary>
    bool TryGetCachedContext(ClientAuthenticationToken token, out DotYouContext context);
        
    /// <summary>
    /// Adds or updates the DotYouContext to cache
    /// </summary>
    /// <param name="token"></param>
    /// <param name="dotYouContext"></param>
    void CacheContext(ClientAuthenticationToken token, DotYouContext dotYouContext);
}