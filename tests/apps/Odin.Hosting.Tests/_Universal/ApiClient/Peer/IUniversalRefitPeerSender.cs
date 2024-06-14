using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Authentication.Owner;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUniversalRefitPeerSender
    {
        private const string RootEndpoint = OwnerApiPathConstants.PeerSenderV1;

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> TransferStream(StreamPart[] streamdata);
        
        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}