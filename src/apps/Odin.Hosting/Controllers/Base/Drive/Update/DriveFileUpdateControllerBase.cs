using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;

namespace Odin.Hosting.Controllers.Base.Drive.Update
{
    /// <summary />
    [ApiController]
    public class DriveFileUpdateControllerBase : DriveUploadControllerBase
    {
        /// <summary>
        /// Uploads a file using multi-part form data
        /// </summary>
        /// <returns></returns>
        [HttpPatch("update")]
        public async Task<FileUpdateResult> UpdateFile()
        {
            if (!IsMultipartContentType(HttpContext.Request.ContentType))
            {
                throw new OdinClientException("Data is not multi-part content", OdinClientErrorCode.MissingUploadData);
            }

            var boundary = GetBoundary(HttpContext.Request.ContentType);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var fileSystemType = this.GetHttpFileSystemResolver().GetFileSystemType();
            var updateWriter = this.GetHttpFileSystemResolver().ResolveFileSystemUpdateWriter();

            var section = await reader.ReadNextSectionAsync();
            AssertIsPart(section, MultipartUploadParts.Instructions);

            string json = await new StreamReader(section!.Body).ReadToEndAsync();
            var instructionSet = OdinSystemSerializer.Deserialize<FileUpdateInstructionSet>(json);
            

            await updateWriter.StartFileUpdateAsync(instructionSet, fileSystemType, WebOdinContext);

            //
            // Firstly, collect everything and store in the temp drive
            //
            section = await reader.ReadNextSectionAsync();
            while (null != section)
            {
                if (IsMetadataPart(section))
                {
                    await updateWriter.AddMetadata(section!.Body, WebOdinContext);
                }

                if (IsPayloadPart(section))
                {
                    AssertIsPayloadPart(section, out var fileSection, out var payloadKey, out var contentTypeFromMultipartSection);
                    await updateWriter.AddPayload(payloadKey, contentTypeFromMultipartSection, fileSection.FileStream, WebOdinContext);
                }

                if (IsThumbnail(section))
                {
                    AssertIsValidThumbnailPart(section, out var fileSection, out var thumbnailUploadKey, out var contentTypeFromMultipartSection);
                    await updateWriter.AddThumbnail(thumbnailUploadKey, contentTypeFromMultipartSection, fileSection.FileStream, WebOdinContext);
                }

                section = await reader.ReadNextSectionAsync();
            }

            var result = await updateWriter.FinalizeFileUpdate(WebOdinContext);
            return result;
        }
    }
}