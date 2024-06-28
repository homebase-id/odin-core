using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.FileSystem;

/// <summary>
/// Composes the elements of a drive system for a given type of file.  Multiple drive systems
/// </summary>
public interface IDriveFileSystem
{
    public DriveQueryServiceBase Query { get; }

    public DriveStorageServiceBase Storage { get; }

}