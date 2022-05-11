using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        T CreateClientUsingAccessToken<T>(DotYouIdentity dotYouId, ClientAuthenticationToken clientAuthenticationToken, Guid? appIdOverride = null);

        T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null);
    }
}