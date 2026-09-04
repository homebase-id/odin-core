using System;
using System.Collections.Generic;
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
        /// the Drives table.  Null on every drive today -- nothing derives a slug or assigns ownership
        /// yet; see <c>docs/drive-addressing.md</c>.
        /// </remarks>
        public Guid? AppId { get; set; }

        /// <summary>
        /// The drive's portable name, and the readable form of its type.
        /// </summary>
        public string DriveSlug { get; set; }

        public string DriveTypeSlug { get; set; }
    }
}