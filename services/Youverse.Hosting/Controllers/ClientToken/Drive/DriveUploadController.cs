﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.Base;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    /// <summary />
    [ApiController]
    [Route(AppApiPathConstants.DrivesV1)]
    [Route(YouAuthApiPathConstants.DrivesV1)]
    [AuthorizeValidExchangeGrant]
    public class DriveUploadController : DriveUploadControllerBase
    {
        
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [SwaggerOperation(Tags = new[] { ControllerConstants.ClientTokenDrive })]
        [HttpPost("files/upload")]
        public async Task<UploadResult> Upload()
        {
            return await base.ReceiveStream();
        }
    }
}