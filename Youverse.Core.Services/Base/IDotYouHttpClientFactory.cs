using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        IPerimeterHttpClient CreateClient(DotYouIdentity dotYouId);
        
        T CreateClient<T>(DotYouIdentity dotYouId, string appIdOverride = null);
    }
}