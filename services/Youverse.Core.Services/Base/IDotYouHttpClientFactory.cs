using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null);

        T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null);
    }
}