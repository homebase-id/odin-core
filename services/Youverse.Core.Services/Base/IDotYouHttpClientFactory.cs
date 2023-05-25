using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Base
{
    public interface IDotYouHttpClientFactory
    {
        T CreateClient<T>(OdinId odinId); 
        
        (T refitClient, Dictionary<string, string> httpHeaders) CreateClientAndHeaders<T>(
            OdinId odinId, 
            ClientAuthenticationToken clientAuthenticationToken = null, 
            FileSystemType? fileSystemType = null);

        Dictionary<string, string> CreateHeaders(
            ClientAuthenticationToken clientAuthenticationToken = null,
            FileSystemType? fileSystemType = null);
    }
}