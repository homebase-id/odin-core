using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Time;
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

        /// <summary>
        /// When the drive was made, Unix ms.  A drive deleted and made again keeps its id and slugs, so this is how
        /// a client tells it from the one before (#2038): compare it for a change, not for order, since the clocks
        /// of the hosts that made the two need not agree.  Set by the V2 drive answers (<see cref="From"/>).
        /// </summary>
        public UnixTimeUtc? Created { get; set; }

        /// <summary>The whole drive, as the V2 answers give it to an owner or an app.</summary>
        public static ClientDriveData From(StorageDrive drive) => new()
        {
            TargetDrive = drive.TargetDriveInfo,
            Name = drive.Name,
            Attributes = drive.Attributes,
            AppId = drive.AppId,
            DriveSlug = drive.DriveSlug,
            DriveTypeSlug = drive.DriveTypeSlug,
            Created = drive.Created
        };
    }
}