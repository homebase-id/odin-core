using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    public class OwnerClientDriveData
    {
        public string Name { get; set; }

        public TargetDrive TargetDriveInfo { get; set; }
        
        public string Metadata { get; set; }
        public bool IsReadonly { get; set; }
        public bool AllowAnonymousReads { get; set; }
        public bool OwnerOnly { get; set; }
    }
}