using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IRefitOwnerTransitSender
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitSenderV1;

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> TransferStream(StreamPart[] streamdata);
        
        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}