using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Storage;

namespace Odin.Core.Services.Base
{
    public interface IOdinHttpClientFactory
    {
        T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null);

        T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null);
    }
}
