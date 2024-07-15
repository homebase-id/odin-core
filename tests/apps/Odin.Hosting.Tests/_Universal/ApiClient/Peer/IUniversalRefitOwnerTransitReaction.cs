using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.Reactions;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUniversalRefitOwnerTransitReaction
    {
        private const string RootEndpoint = OwnerApiPathConstants.PeerReactionContentV1;


    }
}