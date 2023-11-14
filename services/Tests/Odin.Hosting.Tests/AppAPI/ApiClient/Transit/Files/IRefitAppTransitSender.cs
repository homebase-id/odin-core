using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Files
{
    public interface IRefitAppTransitSender
    {
        private const string RootEndpoint = AppApiPathConstants.TransitSenderV1;

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> TransferStream(StreamPart[] parts);

        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteLinkedFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}