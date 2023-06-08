using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Apps;
using Odin.Core.Services.Drives.FileSystem.Base;

namespace Odin.Core.Services.Drives.FileSystem.Standard;

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