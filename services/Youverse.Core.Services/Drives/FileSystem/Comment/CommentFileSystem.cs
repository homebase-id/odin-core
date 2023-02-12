using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Base;

namespace Youverse.Core.Services.Drives.FileSystem.Comment;

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

    public Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
    {
        throw new System.NotImplementedException();
    }
}