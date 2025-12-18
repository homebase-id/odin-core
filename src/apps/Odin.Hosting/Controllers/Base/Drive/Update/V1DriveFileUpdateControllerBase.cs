using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.Management;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.Base.Drive.Update
{
    /// <summary />
    [ApiController]
    public abstract class V1DriveFileUpdateControllerBase(ILogger logger, DriveManager driveManager, FileSystemResolver fileSystemResolver)
        : V1DriveUploadControllerBase(logger, driveManager, fileSystemResolver)
    {
        private readonly ILogger _logger = logger;

        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [HttpPatch("update")]
        public async Task<FileUpdateResult> UpdateFile()
        {
            return await ReceiveFileUpdate();
        }

        [HttpPatch("update-local-metadata-tags")]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataTags([FromBody] UpdateLocalMetadataTagsRequest request)
        {
            //Note: the request.LocalVersionTag might be guid.empty because local content was never written (i.e. a new file)
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            var result = await fs.Storage.UpdateLocalMetadataTags(
                MapToInternalFile(request.File),
                request.LocalVersionTag,
                request.Tags,
                WebOdinContext);

            return result;
        }

        [HttpPatch("update-local-metadata-content")]
        public async Task<UpdateLocalMetadataResult> UpdateLocalMetadataContent([FromBody] UpdateLocalMetadataContentRequest request)
        {
            //Note: the request.LocalVersionTag might be guid.empty because local content was never written (i.e. a new file)
            OdinValidationUtils.AssertIsTrue(request.File.HasValue(), "File is invalid");

            var fs = this.GetHttpFileSystemResolver().ResolveFileSystem();
            var result = await fs.Storage.UpdateLocalMetadataContent(
                MapToInternalFile(request.File),
                request.LocalVersionTag,
                request.Iv,
                request.Content,
                WebOdinContext);

            return result;
        }
    }
}