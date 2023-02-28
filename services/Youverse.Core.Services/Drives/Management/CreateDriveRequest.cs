namespace Youverse.Core.Services.Drives.Management;

public class CreateDriveRequest
{
    public string Name { get; set; }
    public TargetDrive TargetDrive { get; set; }
    public string Metadata { get; set; }
    public bool AllowAnonymousReads { get; set; }
    
    public bool AllowSubscriptions { get; set; }
    public bool OwnerOnly { get; set; }
}