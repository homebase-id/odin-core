using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives
{
    /// <summary>
    /// Information about a drive
    /// </summary>
    [DebuggerDisplay("{Name} AllowAnon={AllowAnonymousReads} AllowSubs={AllowSubscriptions} ReadOnly={IsReadonly}")]
    public sealed class StorageDrive : StorageDriveBase
    {
        private readonly string _tempDataRootPath;
        private readonly string _driveFolderName;
        private readonly string _longTermPayloadPath;

        private readonly StorageDriveBase _inner;

        public StorageDrive(string tempDataRootPath, string longTermPayloadPath, StorageDriveBase inner)
        {
            _inner = inner;
            _driveFolderName = this.Id.ToString("N");
            _tempDataRootPath = Path.Combine(tempDataRootPath, _driveFolderName);

            // value = \data\tenant\payloads\p1\{driveId}\
            // note: p1 is the CIFS mapped drive.
            _longTermPayloadPath = Path.Combine(longTermPayloadPath, _driveFolderName);
        }

        public string DriveFolderName => _driveFolderName;

        public override Guid Id
        {
            get => _inner.Id;
            init { }
        }

        public override string Name
        {
            get => _inner.Name;
            set { }
        }

        public override TargetDrive TargetDriveInfo
        {
            get => _inner.TargetDriveInfo;
            set { }
        }

        public override string Metadata
        {
            get => _inner.Metadata;
            set { }
        }

        public override bool IsReadonly
        {
            get => _inner.IsReadonly;
            set { }
        }

        public override bool AllowSubscriptions
        {
            get => _inner.AllowSubscriptions;
            set => _inner.AllowSubscriptions = value;
        }

        public override SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey
        {
            get => _inner.MasterKeyEncryptedStorageKey;
            set { }
        }

        public override byte[] EncryptedIdIv
        {
            get => _inner.EncryptedIdIv;
            set { }
        }

        public override byte[] EncryptedIdValue
        {
            get => _inner.EncryptedIdValue;
            set { }
        }

        public override bool AllowAnonymousReads
        {
            get => _inner.AllowAnonymousReads;
            set => _inner.AllowAnonymousReads = value;
        }

        public override Dictionary<string, string> Attributes
        {
            get => _inner.Attributes ?? new Dictionary<string, string>();
            set => _inner.Attributes = value;
        }

        public override bool OwnerOnly
        {
            get => _inner.OwnerOnly;
            set { }
        }

        public string GetLongTermPayloadStoragePath()
        {
            return Path.Combine(_longTermPayloadPath, "files");
        }

        public string GetTempStoragePath(TempStorageType storageType)
        {
            switch (storageType)
            {
                case TempStorageType.Upload:
                    return Path.Combine(_tempDataRootPath, "uploads");

                case TempStorageType.Inbox:
                    return Path.Combine(_tempDataRootPath, "inbox");

                default:
                    throw new OdinSystemException($"Unknown storage type:[{storageType}]");
            }
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(this.GetTempStoragePath(TempStorageType.Upload));
            Directory.CreateDirectory(this.GetTempStoragePath(TempStorageType.Inbox));
        }

        public void AssertValidStorageKey(SensitiveByteArray storageKey)
        {
            var decryptedDriveId = AesCbc.Decrypt(this.EncryptedIdValue, storageKey, this.EncryptedIdIv);
            if (!ByteArrayUtil.EquiByteArrayCompare(decryptedDriveId, this.Id.ToByteArray()))
            {
                throw new OdinSecurityException("Invalid key storage attempted to encrypt data");
            }
        }

        public bool AttributeHasTrueValue(string attribute)
        {
            if (null == Attributes)
            {
                return false;
            }

            return this.Attributes.TryGetValue(attribute, out string value) &&
                   bool.TryParse(value, out bool flagValue) &&
                   flagValue;
        }

        public bool IsCollaborationDrive()
        {
            return this.AttributeHasTrueValue(BuiltInDriveAttributes.IsCollaborativeChannel);
        }
    }


    public class StorageDriveBase
    {
        public virtual Guid Id { get; init; }

        public virtual string Name { get; set; }

        /// <summary>
        /// Data specified by the client to further help with usage of this drive (i.e. a json string indicating things like description, etc.)
        /// </summary>
        public virtual string Metadata { get; set; }

        public virtual bool OwnerOnly { get; set; }

        /// <summary>
        /// Specifies a public identifier for accessing this drive.  This stops us from sharing the Id outside of this system.
        /// </summary>
        public virtual TargetDrive TargetDriveInfo { get; set; }

        /// <summary>
        /// Specifies the drive can only be written to by the owner while in the OwnerAuth context
        /// </summary>
        public virtual bool IsReadonly { get; set; }

        /// <summary>
        /// The encryption key used to encrypt the <see cref="ServerFileHeader.EncryptedKeyHeader"/>
        /// </summary>
        public virtual SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey { get; set; }

        public virtual byte[] EncryptedIdIv { get; set; }

        public virtual byte[] EncryptedIdValue { get; set; }

        /// <summary>
        /// Specifies if anonymous callers can read this drive.
        /// </summary>
        public virtual bool AllowAnonymousReads { get; set; }

        /// <summary>
        /// Indicates if the drive allows data subscriptions to be configured.  It is an error
        /// for a drive to be marked OwnerOnly == true and AllowSubscriptions === true
        /// </summary>
        public virtual bool AllowSubscriptions { get; set; }

        public virtual Dictionary<string, string> Attributes { get; set; }
    }
}