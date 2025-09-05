#nullable enable
namespace Odin.Services.Drives;

public struct TempFile
{
    public InternalDriveFileId File { get; set; }
    public TempStorageType StorageType { get; set; }

    public override string ToString()
    {
        return $"File: {File} StorageType: {StorageType}";
    }
}