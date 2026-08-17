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
        public bool IsSystemDrive { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }
}