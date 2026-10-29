using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Apps.Builtin;
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

        /// <summary>
        /// Every payload written to the drive must carry client-computed hashes
        /// </summary>
        public bool RequirePayloadHashes { get; set; }

        public bool OwnerOnly { get; set; }
        public bool IsArchived { get; set; }

        /// <summary>
        /// The app that owns this drive; null means an owner drive.  Null on every drive today --
        /// nothing assigns ownership yet.
        /// </summary>
        public Guid? AppId { get; set; }

        /// <summary>
        /// The drive's portable name, and the readable form of its type, set on every drive since
        /// #1901; see <c>docs/security-model.md, Drive addressing</c>.
        /// </summary>
        public OdinSlug DriveSlug { get; set; }

        public OdinSlug DriveTypeSlug { get; set; }

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

        /// <summary>Who holds which key; see <see cref="DriveSharingModel"/>.</summary>
        public DriveSharingModel SharingModel { get; set; }

        /// <summary>
        /// A zero-knowledge drive's key, locked by the owner's zero-knowledge master key.  The owner's
        /// browser opens it; the server cannot.  Null on every other drive.
        /// </summary>
        public string ZkMasterKeyEncryptedStorageKey { get; set; }

        /// <summary>A zero-knowledge drive's write-only private key, locked by the drive key.  Null on every other drive.</summary>
        public string ZkStorageKeyEncryptedWriteOnlyPrivateKey { get; set; }

        public static OwnerClientDriveData From(StorageDrive drive)
        {
            return new OwnerClientDriveData
            {
                DriveId = drive.Id,
                Name = drive.Name,
                TargetDriveInfo = drive.TargetDriveInfo,
                Metadata = drive.Metadata,
                IsReadonly = drive.IsReadonly,
                AllowAnonymousReads = drive.AllowAnonymousReads,
                AllowSubscriptions = drive.AllowSubscriptions,
                AllowCdn = drive.IsCdnEnabled(),
                RequirePayloadHashes = drive.RequirePayloadHashes,
                OwnerOnly = drive.OwnerOnly,
                Attributes = drive.Attributes,
                IsArchived = drive.IsArchived,
                IsSystemDrive = BuiltinDrives.IsProtected(drive.Id),
                AppId = drive.AppId,
                DriveSlug = drive.DriveSlug,
                DriveTypeSlug = drive.DriveTypeSlug,
                WriteOnlyPublicKeyJwk = drive.WriteOnlyKeyPair?.PublicKeyJwk(),
                WriteOnlyPublicKeyCrc32 = drive.WriteOnlyKeyPair?.crc32c,
                SharingModel = drive.SharingModel,
                ZkMasterKeyEncryptedStorageKey = drive.ZkMasterKeyEncryptedStorageKey,
                ZkStorageKeyEncryptedWriteOnlyPrivateKey = drive.ZkStorageKeyEncryptedWriteOnlyPrivateKey
            };
        }
    }
}