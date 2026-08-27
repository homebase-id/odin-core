using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.OwnerToken.Drive
{
    public class OwnerClientDriveData
    {
        public Guid DriveId { get; set; }
        public string Name { get; set; }

        public TargetDrive TargetDriveInfo { get; set; }

        // added extra field to 
        public TargetDrive TargetDrive => TargetDriveInfo;

        public string Metadata { get; set; }
        public bool IsReadonly { get; set; }
        public bool AllowSubscriptions { get; set; }
        public bool AllowAnonymousReads { get; set; }

        /// <summary>
        /// Whether the CDN may read this drive's payloads. Backs the owner-console checkbox.
        /// </summary>
        public bool AllowCdn { get; set; }

        public bool OwnerOnly { get; set; }
        public bool IsArchived { get; set; }

        /// <summary>
        /// The app that owns this drive; null means an owner drive.  Null on every drive today --
        /// nothing assigns ownership yet.
        /// </summary>
        public Guid? AppId { get; set; }

        /// <summary>
        /// The drive's portable name, and the readable form of its type.  Both null until the
        /// addressing work assigns them; see <c>docs/drive-addressing.md</c>.
        /// </summary>
        public string DriveSlug { get; set; }

        public string DriveTypeSlug { get; set; }
        public bool IsSystemDrive { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }
}