﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Services.Base;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DriveV1)]
    [Route(GuestApiPathConstants.DriveV1)]
    [AuthorizeValidGuestOrAppToken]
    public class ClientTokenDriveUploadController(TenantSystemStorage tenantSystemStorage) : DriveUploadControllerBase
    {
        
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/upload")]
        public async Task<UploadResult> Upload()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.ReceiveFileStream(cn);
        }
        
        /// <summary>
        /// Adds an attachment (thumbnail or payload) to an existing file
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/uploadpayload")]
        public async Task<UploadPayloadResult> UploadPayloadOnly()
        {
            using var cn = tenantSystemStorage.CreateConnection();
            return await base.ReceivePayloadStream(cn);
        }
    }
}