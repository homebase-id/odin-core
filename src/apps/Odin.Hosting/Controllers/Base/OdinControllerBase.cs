using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;

namespace Odin.Hosting.Controllers.Base;

/// <summary>
/// Base utility controller for API endpoints
/// </summary>
public abstract class OdinControllerBase : ControllerBase
{
    /// <summary />
    protected IDriveFileSystem ResolveFileSystem()
    {
        var resolver = this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
        var fst = GetFileSystemType();
        return resolver.ResolveFileSystem(fst);
    }

    protected FileSystemStreamWriterBase ResolveFileSystemWriter()
    {
        var resolver = this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
        var fst = GetFileSystemType();
        return resolver.ResolveFileSystemWriter(fst);
    }

    protected PayloadStreamWriterBase ResolvePayloadStreamWriter()
    {
        var resolver = this.HttpContext.RequestServices.GetRequiredService<FileSystemHttpRequestResolver>();
        var fst = GetFileSystemType();
        return resolver.ResolvePayloadStreamWriter(fst);
    }

    /// <summary />
    protected InternalDriveFileId MapToInternalFile(ExternalFileIdentifier file)
    {
        return new InternalDriveFileId()
        {
            FileId = file.FileId,
            DriveId = OdinContext.PermissionsContext.GetDriveId(file.TargetDrive)
        };
    }

    protected void AddGuestApiCacheHeader(int? minutes = null)
    {
        if (OdinContext.AuthContext == YouAuthConstants.YouAuthScheme || OdinContext.AuthContext == YouAuthConstants.AppSchemeName)
        {
            var seconds = minutes == null ? TimeSpan.FromDays(365).TotalSeconds : TimeSpan.FromMinutes(minutes.GetValueOrDefault()).TotalSeconds;

            this.Response.Headers.TryAdd("Cache-Control", $"max-age={seconds}");
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
    protected OdinContext OdinContext => HttpContext.RequestServices.GetRequiredService<IOdinContextAccessor>().GetCurrent();

    //

    public FileSystemType GetFileSystemType()
    {
        var hasQs = HttpContext.Request.Query.TryGetValue(OdinHeaderNames.FileSystemTypeRequestQueryStringName, out var value);
        if (hasQs)
        {
            if (!Enum.TryParse(typeof(FileSystemType), value, true, out var fst))
            {
                throw new OdinClientException("Invalid file system type specified on query string", OdinClientErrorCode.InvalidFileSystemType);
            }

            return (FileSystemType)fst!;
        }

        //Fall back to the header

        if (!Enum.TryParse(typeof(FileSystemType), HttpContext!.Request.Headers[OdinHeaderNames.FileSystemTypeHeader], true, out var fileSystemType))
        {
            throw new OdinClientException("Invalid file system type or no header specified", OdinClientErrorCode.InvalidFileSystemType);
        }

        return (FileSystemType)fileSystemType!;
    }
}