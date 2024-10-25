#nullable enable
using Odin.Services.Apps;
using Odin.Services.Drives;

public class ClientDriveNotification
{
    public TargetDrive? TargetDrive { get; set; }
    public SharedSecretEncryptedFileHeader? Header { get; set; }
    public SharedSecretEncryptedFileHeader? PreviousServerFileHeader { get; set; }
}