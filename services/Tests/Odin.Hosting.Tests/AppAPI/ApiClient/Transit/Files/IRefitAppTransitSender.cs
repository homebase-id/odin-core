using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Files
{
    public interface IRefitAppTransitSender
    {
        private const string RootEndpoint = AppApiPathConstants.PeerSenderV1;

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> TransferStream(StreamPart[] streamdata);

        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}