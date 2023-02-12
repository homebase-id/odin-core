using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Authentication.ClientToken;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Controllers.Base
{
    /// <summary>
    /// Base class for any endpoint reading drive storage
    /// </summary>
    [ApiController]
    public abstract class DriveReadStorageControllerBase : ControllerBase
    {
        private readonly IAppService _appService;
        private readonly IDriveStorageService _driveStorageService;
        private readonly DotYouContextAccessor _contextAccessor;

        /// <inheritdoc />
        protected DriveReadStorageControllerBase(DotYouContextAccessor contextAccessor, IDriveStorageService driveStorageService, IAppService appService)
        {
            _contextAccessor = contextAccessor;
            _driveStorageService = driveStorageService;
            _appService = appService;
        }

        /// <summary>
        /// Returns the file header
        /// </summary>
        public virtual async Task<IActionResult> GetFileHeader([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };
            var result = await _appService.GetClientEncryptedFileHeader(file);

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
        public virtual async Task<IActionResult> GetPayloadStream([FromBody] ExternalFileIdentifier request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive),
                FileId = request.FileId
            };

            var payload = await _driveStorageService.GetPayloadStream(file);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await _appService.GetClientEncryptedFileHeader(file);
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
        public virtual async Task<IActionResult> GetThumbnail([FromBody] GetThumbnailRequest request)
        {
            var file = new InternalDriveFileId()
            {
                DriveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.File.TargetDrive),
                FileId = request.File.FileId
            };

            var payload = await _driveStorageService.GetThumbnailPayloadStream(file, request.Width, request.Height);
            if (payload == Stream.Null)
            {
                return NotFound();
            }

            var header = await _appService.GetClientEncryptedFileHeader(file);
            string encryptedKeyHeader64 = header.SharedSecretEncryptedKeyHeader.ToBase64();

            HttpContext.Response.Headers.Add(HttpHeaderConstants.PayloadEncrypted, header.FileMetadata.PayloadIsEncrypted.ToString());
            HttpContext.Response.Headers.Add(HttpHeaderConstants.DecryptedContentType, header.FileMetadata.ContentType);
            HttpContext.Response.Headers.Add(HttpHeaderConstants.SharedSecretEncryptedHeader64, encryptedKeyHeader64);

            AddCorsHeader();
            AddCacheHeader();
            return new FileStreamResult(payload, header.FileMetadata.PayloadIsEncrypted ? "application/octet-stream" : header.FileMetadata.ContentType);
        }

        protected void AddCacheHeader()
        {
            if (_contextAccessor.GetCurrent().AuthContext == ClientTokenConstants.YouAuthScheme)
            {
                this.Response.Headers.Add("Cache-Control", "max-age=3600");
            }
        }

        protected void AddCorsHeader()
        {
            var accessor = _contextAccessor.GetCurrent();
            if (accessor.Caller.SecurityLevel != SecurityGroupType.Anonymous)
            {
                HttpContext.Response.Headers.Add("Access-Control-Allow-Origin", accessor.Caller.DotYouId.Id);
            }
        }
    }
}