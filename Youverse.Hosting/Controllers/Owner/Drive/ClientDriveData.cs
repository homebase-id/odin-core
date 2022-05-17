using System;

namespace Youverse.Hosting.Controllers.Owner.Drive
{
    public class ClientDriveData
    {
        public string Name { get; set; }
        public Guid Alias { get; set; }
        public Guid Type { get; set; }
        public string Metadata { get; set; }
        public bool IsReadonly { get; set; }
        public bool AllowAnonymousReads { get; set; }
    }
}