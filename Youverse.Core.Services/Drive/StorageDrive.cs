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
        private readonly string _folderName;
        
        private readonly StorageDriveBase _inner;
        

        public StorageDrive(string longTermDataRootPath, string tempDataRootPath, StorageDriveBase inner)
        {
            _inner = inner;
            
            _folderName = this.Id.ToString("N");
            _tempDataRootPath = Path.Combine(tempDataRootPath, _folderName);
            _longTermDataRootPath = Path.Combine(longTermDataRootPath, _folderName);
        }

        public string LongTermDataRootPath => this._longTermDataRootPath;

        public string TempDataRootPath => this._tempDataRootPath;

        public string FolderName => _folderName;

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
            return path;
        }
    }

    public class StorageDriveBase
    {
        public virtual Guid Id { get; init; }

        public virtual string Name { get; set; }
    }
}