﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Hosting.Controllers.Base.Drive;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    /// <summary/>
    [ApiController]
    [Route(OwnerApiPathConstants.DriveStorageV1)]
    [AuthorizeValidOwnerToken]
    public class OwnerDriveUploadController(ILogger<OwnerDriveUploadController> logger) :
        DriveUploadControllerBase(logger)
    {
        /// <summary/>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveFileStream();
        }

        /// <summary>
        /// Adds/updates a payload to an existing file w/o need to resend the metadata 
        /// </summary>
        [SwaggerOperation(Tags = new[] { ControllerConstants.OwnerDrive })]
        [HttpPost("uploadpayload")]
        public async Task<UploadPayloadResult> UploadPayload()
        {
            return await base.ReceivePayloadStream();
        }
    }
}