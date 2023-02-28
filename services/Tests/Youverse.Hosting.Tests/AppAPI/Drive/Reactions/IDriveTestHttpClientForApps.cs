using System;
using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.Reactions;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.Base;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.AppAPI.Drive.Reactions
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IDriveTestHttpClientForAppReactions
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