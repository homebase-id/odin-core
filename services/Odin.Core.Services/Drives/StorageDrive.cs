using System;
using System.IO;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Drives
{
    /// <summary>
    /// Information about a drive
    /// </summary>
    public sealed class StorageDrive : StorageDriveBase
    {
        private readonly string _longTermDataRootPath;
        private readonly string _tempDataRootPath;
        private readonly string _driveFolderName;
        private readonly string _longTermPayloadPath;

        private readonly StorageDriveBase _inner;

        public StorageDrive(string longTermDataRootPath, string tempDataRootPath, string longTermPayloadPath, StorageDriveBase inner)
        {
            _inner = inner;
            _driveFolderName = this.Id.ToString("N");
            _longTermDataRootPath = Path.Combine(longTermDataRootPath, _driveFolderName);
            _tempDataRootPath = Path.Combine(tempDataRootPath, _driveFolderName);
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
            set { }
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
            set { }
        }

        public override bool OwnerOnly
        {
            get => _inner.OwnerOnly;
            set { }
        }

        // public string GetStoragePath(StorageDisposition storageDisposition)
        // {
        //     var path = storageDisposition == StorageDisposition.Temporary ? this._tempDataRootPath : this._longTermDataRootPath;
        //     return Path.Combine(path, "files");
        // }

        public string GetLongTermHeaderStoragePath()
        {
            return Path.Combine(_longTermDataRootPath, "files");
        }

        public string GetLongTermPayloadStoragePath()
        {
            return Path.Combine(_longTermPayloadPath, "files");
        }

        public string GetTempStoragePath()
        {
            return Path.Combine(_tempDataRootPath, "files");
        }

        public string GetIndexPath()
        {
            return Path.Combine(this._longTermDataRootPath, "idx");
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(this.GetLongTermHeaderStoragePath());
            Directory.CreateDirectory(this.GetTempStoragePath());

            // Directory.CreateDirectory(this.GetPayloadStoragePath());

            Directory.CreateDirectory(this.GetIndexPath());
        }

        public void AssertValidStorageKey(SensitiveByteArray storageKey)
        {
            var decryptedDriveId = AesCbc.Decrypt(this.EncryptedIdValue, ref storageKey, this.EncryptedIdIv);
            if (!ByteArrayUtil.EquiByteArrayCompare(decryptedDriveId, this.Id.ToByteArray()))
            {
                throw new YouverseSecurityException("Invalid key storage attempted to encrypt data");
            }
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
    }
}