using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    public class ClientDriveData
    {

        public TargetDrive TargetDrive { get; set; }
        
        public Dictionary<string,string> Attributes { get; set; }
        public string Name { get; set; }
    }
}