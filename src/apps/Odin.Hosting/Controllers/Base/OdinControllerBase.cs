using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base.Drive;
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
    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        var driveManager = this.HttpContext.RequestServices.GetRequiredService<DriveManager>();
        
        _ = driveManager.GetDriveAsync(file.TargetDrive.Alias, true).GetAwaiter().GetType();

        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = file.TargetDrive.Alias
        };
    }

    protected void AddGuestApiCacheHeader(int? minutes = null)
    {
        if (WebOdinContext.AuthContext == YouAuthConstants.YouAuthScheme || WebOdinContext.AuthContext == YouAuthConstants.AppSchemeName)
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