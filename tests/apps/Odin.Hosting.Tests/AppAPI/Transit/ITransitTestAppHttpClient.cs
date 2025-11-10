using System.Threading.Tasks;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Transit
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitTestAppHttpClient
    {
        private const string RootEndpoint = AppApiPathConstantsV1.PeerV1 + "/app";


        [Post(RootEndpoint + "/process")]
        Task<ApiResponse<InboxStatus>> ProcessInbox([Body] ProcessInboxRequest request);
    }
}