using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive.Core;

namespace Youverse.Core.Services.Drive.Standard;

public class StandardDriveService : IDriveFileService
{
    public StandardDriveService(StandardFileDriveService storageService, DriveQueryService queryService)
    {
        Storage = storageService;
        Query = queryService;
    }

    public IDriveQueryService Query { get; }
    public IDriveService Storage { get; }

    public Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
    {
        throw new System.NotImplementedException();
    }
}