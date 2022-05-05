using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId, bool requireClientAccessToken = false);

        T CreateClientWithAccessToken<T>(DotYouIdentity dotYouId, ClientAuthToken clientAuthToken, Guid? appIdOverride = null);

        T CreateClient<T>(DotYouIdentity dotYouId, Guid? appIdOverride = null, bool requireClientAccessToken = true);
    }
}