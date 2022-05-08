using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId, bool requireClientAccessToken = false);

        T CreateClientWithAccessToken<T>(DotYouIdentity dotYouId, ClientAuthenticationToken clientAuthenticationToken, Guid? appIdOverride = null);

        T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null, bool requireClientAccessToken = true);
    }
}