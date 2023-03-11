using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem.Comment;
using Youverse.Core.Services.Drives.FileSystem.Standard;
using Youverse.Core.Storage;

namespace Youverse.Hosting.Controllers.Base;

/// <summary>
/// Methods to resolve which <see cref="IDriveFileSystem"/> to use based on the
/// <see cref="FileSystemType"/> in the querystring or header.
/// </summary>
public class FileSystemHttpRequestResolver
{
    private readonly IHttpContextAccessor _contextAccessor;

    /// <summary/> 
    public FileSystemHttpRequestResolver(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    /// <summary />
    public FileSystemStreamWriterBase ResolveFileSystemWriter()
    {
        var ctx = _contextAccessor.HttpContext;

        var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return ctx.RequestServices.GetRequiredService<StandardFileStreamWriter>();
        }

        if (fst == FileSystemType.Comment)
        {
            return ctx.RequestServices.GetRequiredService<CommentStreamWriter>();
        }

        throw new YouverseClientException("Invalid file system type or could not parse instruction set", YouverseClientErrorCode.InvalidFileSystemType);
    }

    /// <summary />
    public IDriveFileSystem ResolveFileSystem()
    {
        var ctx = _contextAccessor.HttpContext;

        var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return ctx!.RequestServices.GetRequiredService<StandardFileSystem>();
        }

        if (fst == FileSystemType.Comment)
        {
            return ctx!.RequestServices.GetRequiredService<CommentFileSystem>();
        }

        throw new YouverseClientException("Invalid file system type or could not parse instruction set", YouverseClientErrorCode.InvalidFileSystemType);
    }

    public FileSystemType GetFileSystemType()
    {
        var ctx = _contextAccessor.HttpContext;
        if (!Enum.TryParse(typeof(FileSystemType), ctx!.Request.Headers[DotYouHeaderNames.FileSystemTypeHeader], true, out var fileSystemType))
        {
            throw new YouverseClientException("Invalid file system type or no header specified", YouverseClientErrorCode.InvalidFileSystemType);
        }

        return (FileSystemType)fileSystemType!;
    }
    
}