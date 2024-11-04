using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinHttpClientFactory
    {
        T CreateClientUsingAccessToken<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null);

        T CreateClient<T>(OdinId odinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null);
    }
}