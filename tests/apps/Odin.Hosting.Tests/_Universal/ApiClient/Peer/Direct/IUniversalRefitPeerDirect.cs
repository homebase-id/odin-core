using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IUniversalRefitPeerDirect
    {
        private const string RootEndpoint = "/transit/sender";

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> UploadFile(StreamPart[] streamdata);
        
        [Multipart]
        [Patch(RootEndpoint + "/files/update")]
        Task<ApiResponse<TransitResult>> UpdateFile(StreamPart[] streamdata);
        
        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}