using System;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem.Comment;

public class CommentFileSystem : IDriveFileSystem
{
    public CommentFileSystem(CommentFileStorageService storage, CommentFileQueryService queryService)
    {
        Storage = storage;
        Query = queryService;
    }
    
    public DriveQueryServiceBase Query { get; }
    public DriveStorageServiceBase Storage { get; }

    public DriveCommandServiceBase Commands => throw new NotImplementedException("Commands not supported for comment files");

}