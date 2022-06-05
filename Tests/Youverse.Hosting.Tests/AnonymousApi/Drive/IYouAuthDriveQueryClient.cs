using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Controllers.YouAuth;

namespace Youverse.Hosting.Tests.AnonymousApi.Drive
{
    public interface IYouAuthDriveQueryClient
    {
        private const string RootPath = YouAuthApiPathConstants.DrivesV1 + "/query";
        
        [Post(RootPath + "/recent")]
        Task<ApiResponse<QueryBatchResult>> GetRecent([Query] Guid driveAlias, [Query] UInt64 maxDate, [Query] byte[] startCursor, [Body] QueryParams qp, [Query] ResultOptions options);

        [Post(RootPath + "/batch")]
        Task<ApiResponse<QueryBatchResult>> GetBatch([Query] Guid driveAlias, [Query] byte[] startCursor, [Query] byte[] stopCursor, [Body] QueryParams qp, [Query] ResultOptions options);
    }
}