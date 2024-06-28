using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Apps;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem.Standard;

public class StandardFileSystem : IDriveFileSystem
{
    public StandardFileSystem(StandardFileDriveStorageService storageService, StandardFileDriveQueryService queryService)
    {
        Storage = storageService;
        Query = queryService;
    }
    
    public DriveQueryServiceBase Query { get; }
    public DriveStorageServiceBase Storage { get; }
}