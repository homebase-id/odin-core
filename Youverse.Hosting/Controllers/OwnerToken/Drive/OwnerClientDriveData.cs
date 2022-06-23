using System;

namespace Youverse.Hosting.Controllers.OwnerToken.Drive
{
    public class OwnerClientDriveData
    {
        public string Name { get; set; }
        public Guid Alias { get; set; }
        public Guid Type { get; set; }
        public string Metadata { get; set; }
        public bool IsReadonly { get; set; }
        public bool AllowAnonymousReads { get; set; }
    }
}