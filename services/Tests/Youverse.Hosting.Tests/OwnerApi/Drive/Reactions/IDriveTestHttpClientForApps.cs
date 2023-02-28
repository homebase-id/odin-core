using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.OwnerApi.Drive.Reactions
{
    public interface IDriveTestHttpClientForOwnerReactions
    {
        private const string ReactionRootEndpoint = AppApiPathConstants.DriveReactionsV1;
        
        [Post(ReactionRootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body]AddReactionReqeust request);
        
        [Post(ReactionRootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body]DeleteReactionRequest request);
        
        [Post(ReactionRootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteReactions([Body]DeleteReactionRequest request);
        
        [Post(ReactionRootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Body] ExternalFileIdentifier file);
        
    }
}