using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Core;

namespace Youverse.Core.Services.Drive.Comment;

public class CommentDriveService : IDriveFileService
{
    public CommentDriveService(CommentFileStorageService storageService, CommentFileQueryService queryService)
    {
        Query = queryService;
        Storage = storageService;
    }

    public IDriveQueryService Query { get; }
    public IDriveService Storage { get; }

    public Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
    {
        throw new System.NotImplementedException();
    }
}