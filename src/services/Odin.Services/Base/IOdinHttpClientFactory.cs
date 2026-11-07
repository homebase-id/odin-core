using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinHttpClientFactory
    {
        Task<T> CreateClientUsingAccessTokenAsync<T>(OdinId remoteOdinId, ClientAuthenticationToken clientAuthenticationToken, FileSystemType? fileSystemType = null);

        /// <summary>
        /// As <see cref="CreateClientUsingAccessTokenAsync{T}"/>, for a call that carries a file's payloads: it's given the
        /// time to send them all and for the recipient to store them. Other calls keep the short timeout, so a peer that
        /// has stopped answering doesn't hold them up for this long.
        /// </summary>
        Task<T> CreateFileTransferClientUsingAccessTokenAsync<T>(OdinId remoteOdinId, ClientAuthenticationToken clientAuthenticationToken);

        Task<T> CreateClientAsync<T>(OdinId remoteOdinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null);
    }
}