using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Transit;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient.Transit
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface ITransitTestHttpClientForOwner
    {
        private const string RootEndpoint = OwnerApiPathConstants.TransitSenderV1;

        [Multipart]
        [Post(RootEndpoint + "/files/send")]
        Task<ApiResponse<TransitResult>> TransferStream(StreamPart[] parts);
        
        [Post(RootEndpoint + "/files/senddeleterequest")]
        Task<ApiResponse<DeleteLinkedFileResult>> SendDeleteRequest([Body] DeleteFileByGlobalTransitIdRequest file);
    }
}