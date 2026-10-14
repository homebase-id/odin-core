using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Authentication.Owner;
using Refit;

namespace Odin.Hosting.Tests.V2.DriveDeletion;

/// <summary>The owner-only bulk hard delete, which the universal drive client (shared with apps) does not carry.</summary>
public interface IRefitOwnerDriveDeletion
{
    [Post(OwnerApiPathConstants.DriveStorageV1 + "/harddeletefileidbatch")]
    Task<ApiResponse<HttpContent>> HardDeleteFileIdBatch([Body] DeleteFileIdBatchRequest request);
}
