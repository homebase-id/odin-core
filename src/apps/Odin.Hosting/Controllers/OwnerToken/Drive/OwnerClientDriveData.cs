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

        /// <summary>
        /// The drive's write-only public key, in JWK form -- the half a remote caller seals a deposit
        /// to.  Null on a drive that has no keypair.
        /// </summary>
        /// <remarks>
        /// The <b>public</b> half only.  The private half is escrowed under the drive's storage key and
        /// never leaves the server; serving the public half is the whole point of the key, and the peer
        /// endpoint hands the same value to any caller with write access.
        /// </remarks>
        public string WriteOnlyPublicKeyJwk { get; set; }

        /// <summary>
        /// CRC32C of the public key -- a short fingerprint, so the console can show which key a drive
        /// holds without printing the whole JWK.
        /// </summary>
        public uint? WriteOnlyPublicKeyCrc32 { get; set; }

        public bool IsSystemDrive { get; set; }

        public Dictionary<string, string> Attributes { get; set; }
    }
}