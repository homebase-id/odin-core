using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Apps;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem.Standard;

public class StandardFileSystem(StandardFileDriveStorageService storageService, StandardFileDriveQueryService queryService)
    : IDriveFileSystem
{
    public DriveQueryServiceBase Query { get; } = queryService;
    public DriveStorageServiceBase Storage { get; } = storageService;
}