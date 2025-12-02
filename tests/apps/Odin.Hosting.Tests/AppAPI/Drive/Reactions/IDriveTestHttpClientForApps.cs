using System.Net.Http;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Drive.Reactions
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForAppReactions
    {
        private const string ReactionRootEndpoint = AppApiPathConstantsV1.DriveReactionsV1;
        
        [Post(ReactionRootEndpoint + "/add")]
        Task<ApiResponse<HttpContent>> AddReaction([Body]AddReactionRequest request);
        
        [Post(ReactionRootEndpoint + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteReaction([Body]DeleteReactionRequest request);
        
        [Post(ReactionRootEndpoint + "/deleteall")]
        Task<ApiResponse<HttpContent>> DeleteReactions([Body]DeleteReactionRequest request);
        
        [Post(ReactionRootEndpoint + "/list")]
        Task<ApiResponse<GetReactionsResponse>> GetReactions([Body] ExternalFileIdentifier file);
        
    }
}