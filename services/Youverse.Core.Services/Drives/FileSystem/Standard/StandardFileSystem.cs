using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.Base;

namespace Youverse.Core.Services.Drives.FileSystem.Standard;

public class StandardFileSystem : IDriveFileSystem
{
    public StandardFileSystem(StandardFileDriveStorageService storageService, StandardFileDriveQueryService queryService, StandardDriveCommandService commandService)
    {
        Storage = storageService;
        Query = queryService;
        Commands = commandService;
    }
    
    public DriveQueryServiceBase Query { get; }
    public DriveStorageServiceBase Storage { get; }
    public DriveCommandServiceBase Commands { get; }
    public Task<DeleteLinkedFileResult> DeleteFile(InternalDriveFileId file, List<string> requestRecipients)
    {
        throw new System.NotImplementedException();
    }
}