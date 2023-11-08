using System;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.FileSystem.Base;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base.Drive;

namespace Odin.Hosting.Controllers.Base;

/// <summary>
/// Base utility controller for API endpoints
/// </summary>
public abstract class OdinControllerBase : ControllerBase
{
    /// <summary />
    protected FileSystemHttpRequestResolver GetFileSystemResolver()
    {
        return this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
    }

    /// <summary />
    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        return  new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = OdinContext.PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }
    
    protected void AddGuestApiCacheHeader()
    {
        if (OdinContext.AuthContext == YouAuthConstants.YouAuthScheme)
        {
            this.Response.Headers.Add("Cache-Control", "max-age=3600");
        }
    }
    
    protected FileChunk GetChunk(int? chunkStart, int? chunkLength)
    {
        FileChunk chunk = null;
        if (Request.Headers.TryGetValue("Range", out var rangeHeaderValue) &&
            RangeHeaderValue.TryParse(rangeHeaderValue, out var range))
        {
            var firstRange = range.Ranges.First();
            if (firstRange.From != null && firstRange.To != null)
            {
                HttpContext.Response.StatusCode = 206;

                int start = Convert.ToInt32(firstRange.From ?? 0);
                int end = Convert.ToInt32(firstRange.To ?? int.MaxValue);

                chunk = new FileChunk()
                {
                    Start = start,
                    Length = end - start + 1
                };
            }

            return chunk;
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
    
    /// <summary>
    /// Returns the current DotYouContext from the request
    /// </summary>
    protected OdinContext OdinContext => HttpContext.RequestServices.GetRequiredService<OdinContextAccessor>().GetCurrent();
}