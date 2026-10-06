using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.DriveDeletion;

/// <summary>The owner's drive-deletion endpoints (#1869), as the console calls them.</summary>
public interface IRefitOwnerDriveDeletion
{
    [Post(OwnerApiPathConstants.DriveStorageV1 + "/harddeletefileidbatch")]
    Task<ApiResponse<HttpContent>> HardDeleteFileIdBatch([Body] DeleteFileIdBatchRequest request);

    [Post(OwnerApiPathConstants.DriveManagementV1 + "/empty")]
    Task<ApiResponse<HttpContent>> EmptyDrive([Body] TargetDriveRequest request);

    [Post(OwnerApiPathConstants.DriveManagementV1 + "/delete")]
    Task<ApiResponse<HttpContent>> DeleteDrive([Body] TargetDriveRequest request);
}
