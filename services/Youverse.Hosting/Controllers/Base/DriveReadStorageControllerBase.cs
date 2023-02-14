using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Authentication.ClientToken;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    public abstract class DriveReadStorageControllerBase : YouverseControllerBase
    {
        /// <summary>
        /// Returns the file header
        /// </summary>
        public virtual async Task<IActionResult> GetFileHeader(ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = DotYouContext.PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var result = await this.GetFileSystemResolver().ResolveFileSystem().Storage.GetSharedSecretEncryptedHeader(file);

            if (result == null)
            {
                return NotFound();
            }

            AddCacheHeader();
            return new JsonResult(result);
        }

        /// <summary>
        /// Returns the payload for a given file
        /// </summary>
        public virtual async Task<IActionResult> GetPayloadStream(ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = DotYouContext.PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var fs = this.GetFileSystemResolver().ResolveFileSystem();

            var payload = await fs.Storage.GetPayloadStream(file);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);
            AddCacheHeader();
            AddCorsHeader();
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        /// <summary>
        /// Returns the thumbnail matching the width and height.  Note: you should get the content type from the file header
        /// </summary>
        public virtual async Task<IActionResult> GetThumbnail(GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = DotYouContext.PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            var fs = this.GetFileSystemResolver().ResolveFileSystem();
            var payload = await fs.Storage.GetThumbnailPayloadStream(file, request.Width, request.Height);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await fs.Storage.GetSharedSecretEncryptedHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);

            AddCorsHeader();
            AddCacheHeader();
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        private void AddCacheHeader()
        {
            if (DotYouContext.AuthContext == ClientTokenConstants.YouAuthScheme)
            {
                this.Response.Headers.Add("Cache-Control", "max-age=3600");
            }
        }

        private void AddCorsHeader()
        {
            if (DotYouContext.Caller.SecurityLevel != SecurityGroupType.Anonymous)
            {
                HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", DotYouContext.Caller.DotYouId.Id);
            }
        }
    }
}