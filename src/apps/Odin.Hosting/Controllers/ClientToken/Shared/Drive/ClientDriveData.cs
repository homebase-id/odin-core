using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.ClientToken.Shared.Drive
{
    public class ClientDriveData
    {

        public TargetDrive TargetDrive { get; set; }
        
        public Dictionary<string,string> Attributes { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// The app that owns this drive; null means an owner drive.
        /// </summary>
        /// <remarks>
        /// <see cref="AppId"/>, <see cref="DriveSlug"/> and <see cref="DriveTypeSlug"/> are columns on
        /// the Drives table, set on every drive since #1901; see <c>docs/security-model.md, Drive
        /// addressing</c>.
        /// </remarks>
        public Guid? AppId { get; set; }

        /// <summary>
        /// The drive's portable name, and the readable form of its type.
        /// </summary>
        public OdinSlug DriveSlug { get; set; }

        public OdinSlug DriveTypeSlug { get; set; }
    }
}