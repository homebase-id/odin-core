using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem.Standard;

public class StandardFileSystem(
    StandardFileDriveStorageService storageService,
    StandardFileDriveQueryService queryService,
    StandardDriveCommandService commandService)
    : IDriveFileSystem
{
    public DriveQueryServiceBase Query { get; } = queryService;
    public DriveStorageServiceBase Storage { get; } = storageService;
    public DriveCommandServiceBase Commands { get; } = commandService;
}