using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Hosting.Controllers.Apps;

namespace Youverse.Hosting.Tests.AppAPI.Drive
{
    public interface IDriveQueryClient
    {
        private const string RootPath = AppApiPathConstants.DrivesV1 + "/query";
        
        [Post(RootPath + "/recent")]
        Task<ApiResponse<QueryBatchResult>> GetRecent([Query] TargetDrive drive, [Query] UInt64 maxDate, [Query] byte[] startCursor, [Body] QueryParams qp, [Query] ResultOptions options);

        [Post(RootPath + "/batch")]
        Task<ApiResponse<QueryBatchResult>> GetBatch([Query] TargetDrive drive, [Query] byte[] startCursor, [Query] byte[] stopCursor, [Body] QueryParams qp, [Query] ResultOptions options);
    }
}