using System;
using System.IO;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Information about a drive
    /// </summary>
    public sealed class StorageDrive : StorageDriveBase
    {
        private readonly string _longTermDataRootPath;
        private readonly string _tempDataRootPath;
        private readonly string _driveFolderName;

        private readonly StorageDriveBase _inner;

        public StorageDrive(string longTermDataRootPath, string tempDataRootPath, StorageDriveBase inner)
        {
            _inner = inner;
            _driveFolderName = this.Id.ToString("N");
            _longTermDataRootPath = Path.Combine(longTermDataRootPath, _driveFolderName);
            _tempDataRootPath = Path.Combine(tempDataRootPath, _driveFolderName);
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

        public override Guid Alias
        {
            get => _inner.Alias;
            set { }
        }
        
        public override Guid Type
        {
            get => _inner.Type;
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

        public string GetStoragePath(StorageDisposition storageDisposition)
        {
            var path = storageDisposition == StorageDisposition.Temporary ? this._tempDataRootPath : this._longTermDataRootPath;
            return Path.Combine(path, "files");
        }

        public string GetIndexPath()
        {
            return Path.Combine(this._longTermDataRootPath, "idx");
        }

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(this.GetStoragePath(StorageDisposition.LongTerm));
            Directory.CreateDirectory(this.GetStoragePath(StorageDisposition.Temporary));
            Directory.CreateDirectory(this.GetIndexPath());
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
        
        /// <summary>
        /// Specifies a public identifier for accessing this drive.  This stops us from sharing the Id outside of this system.
        /// </summary>
        public virtual Guid Alias { get; set; }

        /// <summary>
        /// Specifies the type of data stored here.  This field is set by the client when creating a drive.
        /// </summary>
        public virtual Guid Type { get; set; }

        /// <summary>
        /// Specifies the drive can only be written to by the owner while in the OwnerAuth context
        /// </summary>
        public virtual bool IsReadonly { get; set; }

        /// <summary>
        /// The encryption key used to encrypt the <see cref="FilePart.Header"/>
        /// </summary>
        public virtual SymmetricKeyEncryptedAes MasterKeyEncryptedStorageKey { get; set; }

        public virtual byte[] EncryptedIdIv { get; set; }

        public virtual byte[] EncryptedIdValue { get; set; }

        /// <summary>
        /// Specifies if anonymous callers can read this drive.
        /// </summary>
        public virtual bool AllowAnonymousReads { get; set; }
        
    }
}