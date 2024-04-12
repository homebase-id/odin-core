using System;
using Microsoft.AspNetCore.Http;
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

namespace Odin.Hosting.Controllers.Base.Drive;

/// <summary>
/// Methods to resolve which <see cref="IDriveFileSystem"/> to use based on the
/// <see cref="FileSystemType"/> in the querystring or header.
/// </summary>
public class FileSystemHttpRequestResolver
{
    private readonly StandardFileSystem _standardFileSystem;
    private readonly CommentFileSystem _commentFileSystem;

    private readonly StandardFileStreamWriter _standardFileStreamWriter;
    private readonly CommentStreamWriter _commentStreamWriter;

    private readonly StandardFilePayloadStreamWriter _standardFilePayloadStreamWriter;
    private readonly CommentPayloadStreamWriter _commentPayloadStreamWriter;

    /// <summary/> 
    public FileSystemHttpRequestResolver(StandardFileSystem standardFileSystem, CommentFileSystem commentFileSystem,
        CommentStreamWriter commentStreamWriter, StandardFileStreamWriter standardFileStreamWriter,
        StandardFilePayloadStreamWriter standardFilePayloadStreamWriter, CommentPayloadStreamWriter commentPayloadStreamWriter)
    {
        _standardFileSystem = standardFileSystem;
        _commentFileSystem = commentFileSystem;
        _commentStreamWriter = commentStreamWriter;
        _standardFileStreamWriter = standardFileStreamWriter;
        _standardFilePayloadStreamWriter = standardFilePayloadStreamWriter;
        _commentPayloadStreamWriter = commentPayloadStreamWriter;
    }

    public PayloadStreamWriterBase ResolvePayloadStreamWriter(FileSystemType fst)
    {

        if (fst == FileSystemType.Standard)
        {
            return _standardFilePayloadStreamWriter;
        }

        if (fst == FileSystemType.Comment)
        {
            return _commentPayloadStreamWriter;
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }

    /// <summary />
    public FileSystemStreamWriterBase ResolveFileSystemWriter(FileSystemType fst)
    {
        if (fst == FileSystemType.Standard)
        {
            return _standardFileStreamWriter;
        }

        if (fst == FileSystemType.Comment)
        {
            return _commentStreamWriter;
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }

    /// <summary />
    public IDriveFileSystem ResolveFileSystem(FileSystemType fst)
    {
        // var fst = GetFileSystemType();

        if (fst == FileSystemType.Standard)
        {
            return _standardFileSystem;
        }

        if (fst == FileSystemType.Comment)
        {
            return _commentFileSystem;
        }

        throw new OdinClientException("Invalid file system type or could not parse instruction set", OdinClientErrorCode.InvalidFileSystemType);
    }
}