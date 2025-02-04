using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Drives.FileSystem.Comment;
using Odin.Services.Drives.FileSystem.Comment.Attachments;
using Odin.Services.Drives.FileSystem.Standard;
using Odin.Services.Drives.FileSystem.Standard.Attachments;
using Odin.Core.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Comment.Update;
using Odin.Services.Drives.FileSystem.Standard.Update;

namespace Odin.Hosting.Controllers.Base.Drive;

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

    public PayloadStreamWriterBase ResolvePayloadStreamWriter()
    {
        var ctx = _contextAccessor.HttpContext;

        var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return ctx!.RequestServices.GetRequiredService<StandardFilePayloadStreamWriter>();
        }

        if (fst == FileSystemType.Comment)
        {
            return ctx!.RequestServices.GetRequiredService<CommentPayloadStreamWriter>();
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }
    
    /// <summary />
    public FileSystemStreamWriterBase ResolveFileSystemWriter()
    {
        var ctx = _contextAccessor.HttpContext;

        var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return ctx!.RequestServices.GetRequiredService<StandardFileStreamWriter>();
        }

        if (fst == FileSystemType.Comment)
        {
            return ctx!.RequestServices.GetRequiredService<CommentStreamWriter>();
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }

    
    public FileSystemUpdateWriterBase ResolveFileSystemUpdateWriter()
    {
        var ctx = _contextAccessor.HttpContext;

        var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return ctx!.RequestServices.GetRequiredService<StandardFileUpdateWriter>();
        }

        if (fst == FileSystemType.Comment)
        {
            return ctx!.RequestServices.GetRequiredService<CommentFileUpdateWriter>();
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
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

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }

    public FileSystemType GetFileSystemType()
    {
        var ctx = _contextAccessor.HttpContext!;

        //first try to get file system type by qs
        var hasQs = ctx.Request.Query.TryGetValue(OdinHeaderNames.FileSystemTypeRequestQueryStringName, out var value);
        if (hasQs)
        {
            if (!Enum.TryParse(typeof(FileSystemType), value, true, out var fst))
            {
                throw new OdinClientException("Invalid file system type specified on query string", OdinClientErrorCode.InvalidFileSystemType);
            }
            
            return (FileSystemType)fst!;
        }

        //Fall back to the header

        if (!Enum.TryParse(typeof(FileSystemType), ctx!.Request.Headers[OdinHeaderNames.FileSystemTypeHeader], true, out var fileSystemType))
        {
            throw new OdinClientException("Invalid file system type or no header specified", OdinClientErrorCode.InvalidFileSystemType);
        }

        return (FileSystemType)fileSystemType!;
    }
}