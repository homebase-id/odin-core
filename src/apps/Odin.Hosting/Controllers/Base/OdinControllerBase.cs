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
using Odin.Services.Apps;
using Odin.Services.Drives.DriveCore.Storage;
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

    /// <summary>
    /// True when the caller is the identity owner or one of the owner's own apps -- the viewers entitled
    /// to see what the owner thinks about a contact, as opposed to a third party who may merely be allowed
    /// to see that the contact exists.
    /// </summary>
    /// <remarks>
    /// Several endpoints are mounted on both an owner/app route and the guest (YouAuth) route, and a guest
    /// can hold <c>ReadConnections</c> whenever the tenant setting allows it.  Permission to read the list
    /// is not permission to read the owner's judgments about the people on it, so those endpoints project a
    /// narrower shape when this is false (docs/connection-defaults.md, "Viewer-scoped redaction").
    ///
    /// <para>
    /// Both auth generations have to be asked: V1 carries the scheme in <c>AuthContext</c>, V2 in the
    /// caller's <see cref="ClientTokenType"/>.  Same pairing as the guest-cache header below.
    /// </para>
    /// </remarks>
    protected bool CallerIsOwnerSideViewer
    {
        get
        {
            if (WebOdinContext.Caller.IsOwner)
            {
                return true;
            }

            return WebOdinContext.Caller.ClientTokenType == ClientTokenType.App ||
                   WebOdinContext.AuthContext == YouAuthConstants.AppSchemeName;
        }
    }

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
        var seconds = minutes == null
            ? (long)TimeSpan.FromDays(365).TotalSeconds
            : (long)TimeSpan.FromMinutes(minutes.GetValueOrDefault()).TotalSeconds;

        AddGuestApiCacheHeaderSeconds(seconds);
    }

    /// <summary>
    /// Caches a file's bytes for no longer than the file itself will live.
    ///
    /// The default here is a year, which is wrong for anything that expires: the file would be deleted
    /// on schedule and go on being served from browser and edge caches long afterwards. Clamping to the
    /// remaining lifetime keeps the cache useful without letting it outlive its subject.
    /// </summary>
    protected void AddGuestApiCacheHeaderForFile(long ttl, UnixTimeUtc created)
    {
        // A pending (expire-after-first-read) Ttl is still negative on the header the controller
        // fetched, because that read happened before the payload read resolved it. By the time this
        // response goes out the clock has started, so the real remaining life is |Ttl| - not the
        // unread backstop that ExpiresAt would report. Cache for exactly that window: the cache then
        // expires when the file does, which is what makes a CDN read equivalent to a direct one.
        if (FileTtl.IsPendingFirstRead(ttl))
        {
            AddGuestApiCacheHeaderSeconds(Math.Abs(ttl) / 1000);
            return;
        }

        var dueAt = FileTtl.ExpiresAt(ttl, created);
        if (dueAt == null)
        {
            AddGuestApiCacheHeader();
            return;
        }

        var remainingSeconds = (dueAt.Value - UnixTimeUtc.Now().milliseconds) / 1000;
        AddGuestApiCacheHeaderSeconds(Math.Max(remainingSeconds, 0));
    }

    private void AddGuestApiCacheHeaderSeconds(long seconds)
    {
        var isYouAuthV2 = WebOdinContext.Caller.ClientTokenType == ClientTokenType.YouAuth;
        var isYouAuthV1 = WebOdinContext.AuthContext == YouAuthConstants.YouAuthScheme;
        var isYouAuth = isYouAuthV1 || isYouAuthV2;

        var isAppAuthV2 = WebOdinContext.Caller.ClientTokenType == ClientTokenType.App;
        var isAppAuthV1 = WebOdinContext.AuthContext == YouAuthConstants.AppSchemeName;
        var isAppAuth = isAppAuthV2 || isAppAuthV1;

        // The CDN edge needs this more than anyone: it caches on behalf of every downstream reader, so
        // without an explicit max-age it decides for itself how long to keep a payload that the origin
        // knows is expiring. Reading via the CDN should mean the same as reading directly, and that
        // includes the edge copy not outliving the file.
        var isCdn = WebOdinContext.Caller.ClientTokenType == ClientTokenType.Cdn;

        if (isYouAuth || isAppAuth || isCdn)
        {
            Response.Headers.TryAdd("Cache-Control", $"max-age={seconds}");
        }
    }

    /// <summary>
    /// The requested byte range: a Range header wins over the query/route values. No length (or 0) means "to the end".
    /// </summary>
    protected FileChunk GetChunk(Int64? chunkStart, Int64? chunkLength)
    {
        if (Request.Headers.TryGetValue("Range", out var rangeHeaderValue) &&
            RangeHeaderValue.TryParse(rangeHeaderValue, out var range))
        {
            var firstRange = range.Ranges.First();
            if (firstRange.From != null)
            {
                HttpContext.Response.StatusCode = 206;

                // To - From cannot overflow (both are non-negative); adding 1 can, for an end of long.MaxValue,
                // which only means "to the end" anyway
                var start = firstRange.From.Value;
                var span = firstRange.To - start;
                return new FileChunk()
                {
                    Start = start,
                    Length = span == null || span == Int64.MaxValue ? null : span + 1
                };
            }

            return null;
        }
        else if (chunkStart.HasValue)
        {
            // A route has no way to leave the length out, so 0 says "to the end" there, as it always has
            return new FileChunk()
            {
                Start = chunkStart.Value,
                Length = chunkLength is null or 0 ? null : chunkLength
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
        if (payloadStream.Range != null)
        {
            HttpContext.Response.Headers.ContentRange = ContentRange(payloadStream);
        }

        return new FileStreamResult(payloadStream.Stream, "application/octet-stream");
    }

    /// <summary>
    /// The Content-Range header of a ranged payload stream (its <see cref="PayloadStream.Range"/> is set).
    /// </summary>
    protected static string ContentRange(PayloadStream payloadStream)
    {
        var range = payloadStream.Range!;
        return new ContentRangeHeaderValue(range.Start, range.Start + range.Length!.Value - 1, payloadStream.PayloadSize).ToString();
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