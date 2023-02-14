using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base class for querying a drive's search index
    /// </summary>
    public class DriveQueryControllerBase : YouverseControllerBase
    {
        public virtual async Task<QueryModifiedResult> QueryModified([FromBody] QueryModifiedRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetFileSystemResolver().ResolveFileSystem().Query.GetModified(driveId, request.QueryParams, request.ResultOptions);
            return batch;
        }

        public virtual async Task<QueryBatchResponse> QueryBatch([FromBody] QueryBatchRequest request)
        {
            var driveId = DotYouContext.PermissionsContext.GetDriveId(request.QueryParams.TargetDrive);
            var batch = await GetFileSystemResolver().ResolveFileSystem().Query.GetBatch(driveId, request.QueryParams, request.ResultOptionsRequest.ToQueryBatchResultOptions());

            return QueryBatchResponse.FromResult(batch);
        }

        public override SignInResult SignIn(ClaimsPrincipal principal, AuthenticationProperties properties, string authenticationScheme)
        {
            return base.SignIn(principal, properties, authenticationScheme);
        }

        public virtual async Task<QueryBatchCollectionResponse> QueryBatchCollection([FromBody] QueryBatchCollectionRequest request)
        {
            var collection = await GetFileSystemResolver().ResolveFileSystem().Query.GetBatchCollection(request);
            return collection;
        }
    }
}