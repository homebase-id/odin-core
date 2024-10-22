using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary/>
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveUploadController(TenantSystemStorage tenantSystemStorage) : DriveUploadControllerBase
    {
        /// <summary/>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("upload")]
        public async Task<UploadResult> Upload()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.ReceiveFileStream(db);
        }

        /// <summary>
        /// Adds/updates a payload to an existing file w/o need to resend the metadata 
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("uploadpayload")]
        public async Task<UploadPayloadResult> UploadPayload()
        {
            var db = tenantSystemStorage.IdentityDatabase;
            return await base.ReceivePayloadStream(db);
        }
    }
}