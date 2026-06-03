using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer.Encryption;
using Odin.Services.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Controllers.Base;

/// <summary>
/// Base utility controller for API endpoints
/// </summary>
public abstract class OdinControllerBase : ControllerBase
{
    private IOdinContext _odinContext;

    /// <summary />
    protected FileSystemHttpRequestResolver GetHttpFileSystemResolver()
    {
        return this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
    }

    protected async Task AddUpgradeRequiredHeaderAsync()
    {
        var scheduler = this.HttpContext.RequestServices.GetRequiredService<VersionUpgradeScheduler>();
        var (upgradeRequired, tenantVersion, failureInfo) = await scheduler.RequiresUpgradeAsync();
        if (upgradeRequired)
        {
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<OdinControllerBase>>();
            logger.LogDebug("Upgrade test indicated that upgrade is required.  " +
                            "It will be scheduled only when you are running as owner " +
                            "Tenant is on v{cv} while release version is v{rv} " +
                            "(previously failed build version: {failure})",
                tenantVersion,
                Odin.Services.Version.DataVersionNumber,
                failureInfo?.BuildVersion ?? "none");

            VersionUpgradeScheduler.SetRequiresUpgradeResponse(HttpContext);
        }
    }
    
    /// <summary />
    protected async Task<InternalDriveFileId> MapToInternalFileAsync(ExternalFileIdentifier file)
    {
        var driveManager = HttpContext.RequestServices.GetRequiredService<DriveManager>();
        
        // Validates the drive exists
        await driveManager.GetDriveAsync(file.TargetDrive.Alias, failIfInvalid: true);

        OdinValidationUtils.AssertNotEmptyGuid(file.TargetDrive.Alias, "Target drive alias is required");
        
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = file.TargetDrive.Alias
        };
    }

    protected void AddGuestApiCacheHeader(int? minutes = null)
    {
        var isYouAuthV2 = WebOdinContext.Caller.ClientTokenType == ClientTokenType.YouAuth;
        var isYouAuthV1 = WebOdinContext.AuthContext == YouAuthConstants.YouAuthScheme;
        var isYouAuth = isYouAuthV1 || isYouAuthV2;
        
        var isAppAuthV2 = WebOdinContext.Caller.ClientTokenType == ClientTokenType.App;
        var isAppAuthV1 = WebOdinContext.AuthContext == YouAuthConstants.AppSchemeName;
        var isAppAuth = isAppAuthV2 || isAppAuthV1;
        
        if (isYouAuth || isAppAuth)
        {
            var seconds = minutes == null
                ? TimeSpan.FromDays(365).TotalSeconds
                : TimeSpan.FromMinutes(minutes.GetValueOrDefault()).TotalSeconds;
            Response.Headers.TryAdd("Cache-Control", $"max-age={seconds}");
        }
    }

    protected FileChunk GetChunk(int? chunkStart, int? chunkLength)
    {
        if (Request.Headers.TryGetValue("Range", out var rangeHeaderValue) &&
            RangeHeaderValue.TryParse(rangeHeaderValue, out var range))
        {
            var firstRange = range.Ranges.First();
            if (firstRange.From != null)
            {
                HttpContext.Response.StatusCode = 206;

                int start = Convert.ToInt32(firstRange.From ?? 0);
                if (firstRange.To == null)
                {
                    return new FileChunk()
                    {
                        Start = start,
                        Length = int.MaxValue
                    };
                }

                int end = Convert.ToInt32(firstRange.To);

                return new FileChunk()
                {
                    Start = start,
                    Length = end - start + 1
                };
            }

            return null;
        }
        else if (chunkStart.HasValue)
        {
            return new FileChunk()
            {
                Start = chunkStart.GetValueOrDefault(),
                Length = chunkLength.GetValueOrDefault(int.MaxValue)
            };
        }

        return null;
    }

    protected void AssertIsValidOdinId(string odinId, out OdinId id)
    {
        OdinValidationUtils.AssertIsValidOdinId(odinId, out id);
    }

    /// <summary>
    /// Renders a payload stream retrieved from a peer identity as an HTTP response, populating the
    /// shared-secret / content-type headers the client needs to decrypt the payload.
    /// </summary>
    protected IActionResult HandlePeerPayloadResponse(EncryptedKeyHeader encryptedKeyHeader, bool isEncrypted,
        PayloadStream payloadStream)
    {
        if (payloadStream == null)
        {
            return NotFound();
        }

        AddGuestApiCacheHeader();

        HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
        HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadKey, payloadStream.Key);
        HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(payloadStream.LastModified);
        HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, payloadStream.ContentType);
        HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, encryptedKeyHeader.ToBase64());
        HttpContext.Response.Headers.ContentLength = payloadStream.ContentLength;
        return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
    }

    /// <summary>
    /// Renders a thumbnail stream retrieved from a peer identity as an HTTP response, populating the
    /// shared-secret / content-type headers the client needs to decrypt the thumbnail.
    /// </summary>
    protected IActionResult HandlePeerThumbnailResponse(EncryptedKeyHeader encryptedKeyHeader, bool isEncrypted,
        string decryptedContentType, UnixTimeUtc? lastModified, Stream thumb)
    {
        if (thumb == Stream.Null)
        {
            return NotFound();
        }

        AddGuestApiCacheHeader();

        HttpContext.Response.Headers.Append(HttpHeaderConstants.PayloadEncrypted, isEncrypted.ToString());
        HttpContext.Response.Headers.Append(HttpHeaderConstants.DecryptedContentType, decryptedContentType);
        HttpContext.Response.Headers.LastModified = DriveFileUtility.GetLastModifiedHeaderValue(lastModified);
        HttpContext.Response.Headers.Append(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64, encryptedKeyHeader.ToBase64());
        return new FileStreamResult(thumb, "application/octet-stream");
    }

    /// <summary>
    /// Returns the current DotYouContext from the request
    /// </summary>
    protected IOdinContext WebOdinContext
    {
        get
        {
            if (_odinContext != null)
            {
                return _odinContext;
            }

            _odinContext = HttpContext.RequestServices.GetRequiredService<IOdinContext>();
            if (string.IsNullOrEmpty(_odinContext.Tenant))
            {
                throw new OdinSystemException("Missing IOdinContext.Tenant");
            }

            return _odinContext;
        }
    }
}