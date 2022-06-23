using System;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    public class ClientDriveData
    {
        public string Name { get; set; }
        public Guid Alias { get; set; }
        public Guid Type { get; set; }
        public string Metadata { get; set; }
    }
}