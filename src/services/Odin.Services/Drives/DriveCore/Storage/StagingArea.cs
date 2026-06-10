namespace Odin.Services.Drives.DriveCore.Storage;

/// Identifies which temporary staging area a file lives in, carrying the backend
/// implicitly (resolved to a concrete IDriveFileStore in DriveStorageServiceBase).
public enum StagingArea
{
    Upload,
    Inbox
}
