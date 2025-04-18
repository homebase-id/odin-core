using System;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem.Comment;

public class CommentFileSystem(CommentFileStorageService storage, CommentFileQueryService queryService)
    : IDriveFileSystem
{
    public DriveQueryServiceBase Query { get; } = queryService;
    public DriveStorageServiceBase Storage { get; } = storage;
}