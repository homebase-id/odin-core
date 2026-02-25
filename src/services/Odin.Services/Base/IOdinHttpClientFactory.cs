using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinHttpClientFactory
    {
        Task<T> CreateClientUsingAccessTokenAsync<T>(OdinId odinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null);

        Task<T> CreateClientAsync<T>(OdinId odinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null);
    }
}