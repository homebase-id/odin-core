using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinHttpClientFactory
    {
        /// <param name="timeout">The request's whole time, instead of HttpClient's default of 100 s: a file send's, sized by
        /// its payloads (<see cref="Peer.Outgoing.Drive.Transfer.Outbox.PeerFileSendTimeout"/>).</param>
        Task<T> CreateClientUsingAccessTokenAsync<T>(OdinId remoteOdinId, ClientAuthenticationToken clientAuthenticationToken,
            FileSystemType? fileSystemType = null, TimeSpan? timeout = null);

        Task<T> CreateClientAsync<T>(OdinId remoteOdinId, FileSystemType? fileSystemType = null, Dictionary<string, string> headers = null);
    }
}