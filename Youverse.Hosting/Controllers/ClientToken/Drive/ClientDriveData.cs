using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.ClientToken.Drive
{
    public class ClientDriveData
    {
        public string Name { get; set; }

        public TargetDrive TargetDrive { get; set; }
        public string Metadata { get; set; }
    }
}