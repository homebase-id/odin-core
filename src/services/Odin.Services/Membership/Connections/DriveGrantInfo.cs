using Odin.Services.Drives;

namespace Odin.Services.Membership.Connections;

public class DriveGrantInfo
{
    public string DriveName { get; set; }
    public bool DriveGrantIsValid { get; set; }
    public bool DriveIsGranted { get; set; }
    
    public DrivePermission ExpectedDrivePermission { get; set; }
    public DrivePermission ActualDrivePermission { get; set; }
    public int EncryptedKeyLength { get; set; }
}