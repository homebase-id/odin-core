using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive;

public class CreateDriveRequest
{
    public string Name { get; set; }
    public TargetDrive TargetDrive { get; set; }
    public string Metadata { get; set; }
    public bool AllowAnonymousReads { get; set; }
}