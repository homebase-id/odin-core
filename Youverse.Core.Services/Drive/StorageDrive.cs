using System;
using System.IO;
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
    }
}