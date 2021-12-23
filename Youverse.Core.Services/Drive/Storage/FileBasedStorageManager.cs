using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Drive.Storage
{
    public class FileBasedStorageManager : IStorageManager
    {
        private readonly ILogger<IStorageManager> _logger;

        private readonly StorageDrive _drive;
        private const int WriteChunkSize = 1024;

        private readonly StorageDriveIndex _primaryIndex;
        private readonly StorageDriveIndex _secondaryIndex;

        private StorageDriveIndex _currentIndex;
        private bool _isRebuilding;
        private IndexReadyState _indexReadyState;

        public FileBasedStorageManager(StorageDrive drive, ILogger<IStorageManager> logger)
        {
            Guard.Argument(drive, nameof(drive)).NotNull();
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.LongTermDataRootPath), sd => $"No directory for drive storage at {sd.LongTermDataRootPath}");
            // Guard.Argument(drive, nameof(drive)).Require(sd => Directory.Exists(sd.TempDataRootPath), sd => $"No directory for drive storage at {sd.TempDataRootPath}");

            Directory.CreateDirectory(drive.LongTermDataRootPath);
            Directory.CreateDirectory(drive.TempDataRootPath);
            _logger = logger;
            _drive = drive;

            _primaryIndex = new StorageDriveIndex(IndexTier.Primary, _drive.LongTermDataRootPath);
            _secondaryIndex = new StorageDriveIndex(IndexTier.Secondary, _drive.LongTermDataRootPath);
        }

        public StorageDrive Drive => _drive;

        public Guid CreateFileId()
        {
            //TODO: Create a datetime-based
            return Guid.NewGuid();
        }

        public Task WritePartStream(Guid fileId, FilePart part, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            //TODO: this is probably highly inefficient and probably need to revisit 
            string filePath = GetFilePath(fileId, part, storageDisposition, true);
            string tempFilePath = GetTempFilePath(fileId, part, storageDisposition);
            try
            {
                if (File.Exists(filePath))
                {
                    WriteStream(stream, tempFilePath);
                    lock (filePath)
                    {
                        //TODO: need to know if this replace method is faster than renaming files
                        File.Replace(tempFilePath, filePath, null, true);
                    }
                }
                else
                {
                    WriteStream(stream, filePath);
                }
            }
            finally
            {
                //TODO: should clean up the temp file in case of failure?
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }

            return Task.CompletedTask;
        }

        public Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            string path = GetFilePath(fileId, filePart, storageDisposition);
            var fileStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
            return Task.FromResult((Stream)fileStream);
        }

        public Task<StorageDisposition> GetStorageType(Guid fileId)
        {
            //just check for the header, this assumes the file is valid
            var longTermPath = GetFilePath(fileId, FilePart.Header, StorageDisposition.LongTerm);

            if (File.Exists(longTermPath))
            {
                return Task.FromResult(StorageDisposition.LongTerm);
            }

            var tempPath = GetFilePath(fileId, FilePart.Header, StorageDisposition.Temporary);
            if (File.Exists(tempPath))
            {
                return Task.FromResult(StorageDisposition.Temporary);
            }

            return Task.FromResult(StorageDisposition.Unknown);
        }

        public async Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            await using var stream = File.Open(GetFilePath(fileId, FilePart.Header, storageDisposition), FileMode.Open, FileAccess.Read);
            var json = await new StreamReader(stream).ReadToEndAsync();
            stream.Close();

            var ekh = JsonConvert.DeserializeObject<EncryptedKeyHeader>(json);
            return ekh;
        }

        public async Task WriteKeyHeader(Guid fileId, EncryptedKeyHeader keyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            var json = JsonConvert.SerializeObject(keyHeader);
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            await this.WritePartStream(fileId, FilePart.Header, stream, storageDisposition);
            stream.Close();
        }

        public void AssertFileIsValid(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            if (fileId == Guid.Empty)
            {
                throw new Exception("No file specified");
            }

            //check both
            if (storageDisposition == StorageDisposition.Unknown)
            {
                if (IsFileValid(fileId, StorageDisposition.LongTerm))
                {
                    return;
                }

                if (IsFileValid(fileId, StorageDisposition.Temporary))
                {
                    return;
                }
            }

            if (!IsFileValid(fileId, storageDisposition))
            {
                throw new Exception("File does not contain all parts");
            }
        }

        private bool IsFileValid(Guid fileId, StorageDisposition storageDisposition)
        {
            string header = GetFilePath(fileId, FilePart.Header, storageDisposition);
            string metadata = GetFilePath(fileId, FilePart.Metadata, storageDisposition);
            string payload = GetFilePath(fileId, FilePart.Payload, storageDisposition);

            return File.Exists(header) && File.Exists(metadata) && File.Exists(payload);
        }

        public Task Delete(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            string header = GetFilePath(fileId, FilePart.Header, storageDisposition);
            if (File.Exists(header))
            {
                File.Delete(header);
            }

            string metadata = GetFilePath(fileId, FilePart.Metadata, storageDisposition);
            if (File.Exists(metadata))
            {
                File.Delete(metadata);
            }

            string payload = GetFilePath(fileId, FilePart.Payload, storageDisposition);

            if (File.Exists(payload))
            {
                File.Delete(payload);
            }

            return Task.CompletedTask;
        }

        public Task MoveToLongTerm(Guid fileId)
        {
            AssertFileIsValid(fileId, StorageDisposition.Temporary);

            var parts = Enum.GetNames<FilePart>();
            foreach (var p in parts)
            {
                FilePart part = Enum.Parse<FilePart>(p);
                var source = GetFilePath(fileId, part, StorageDisposition.Temporary);
                var dest = GetFilePath(fileId, part, StorageDisposition.LongTerm, ensureExists: true);

                File.Move(source, dest);

                _logger.LogInformation($"File Moved to {dest}");
            }

            return Task.CompletedTask;
        }

        public Task MoveToTemp(Guid fileId)
        {
            var parts = Enum.GetNames<FilePart>();
            foreach (var p in parts)
            {
                FilePart part = Enum.Parse<FilePart>(p);
                var source = GetFilePath(fileId, part, StorageDisposition.LongTerm);
                var dest = GetFilePath(fileId, part, StorageDisposition.Temporary, ensureExists: true);
                File.Move(source, dest);
            }

            return Task.CompletedTask;
        }

        public Task<long> GetFileSize(Guid id, StorageDisposition storageDisposition = StorageDisposition.LongTerm)
        {
            //TODO: make more efficient by reading metadata or something else?
            var path = GetFilePath(id, FilePart.Payload, storageDisposition);
            return Task.FromResult(new FileInfo(path).Length);
        }

        private string GetFileDirectory(Guid id, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string path = _drive.GetStoragePath(storageDisposition);
            string dir = PathUtil.Combine(path, id.ToString());

            if (ensureExists)
            {
                Directory.CreateDirectory(dir);
            }

            return dir;
        }

        private string GetFilePath(Guid id, FilePart part, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string dir = GetFileDirectory(id, storageDisposition, ensureExists);
            return PathUtil.Combine(dir, part.ToString());
        }

        private string GetTempFilePath(Guid id, FilePart part, StorageDisposition storageDisposition, bool ensureExists = false)
        {
            string dir = GetFileDirectory(id, storageDisposition, ensureExists);
            string filename = $"{Guid.NewGuid()}{part}.tmp";
            return Path.Combine(dir, filename);
        }

        public async Task Rebuild(StorageDriveIndex index)
        {
            if (Directory.Exists(index.IndexRootPath))
            {
                Directory.Delete(index.IndexRootPath, true);
            }

            //Directory.CreateDirectory(index.IndexRootPath);


            // var fileList = _storageManager.GetFileList();
            // foreach (var file in fileList)
            // {
            //     //  read permission from the fileid.acl
            //     //      create IndexedItemPermission
            //     //  create IndexedItem
            // }
        }

        public async Task RebuildIndex()
        {
            //TODO: add locking?

            if (_isRebuilding)
            {
                return;
            }

            _isRebuilding = true;
            StorageDriveIndex indexToRebuild;
            if (_currentIndex == null)
            {
                indexToRebuild = _primaryIndex;
            }
            else
            {
                indexToRebuild = _currentIndex.Tier == _primaryIndex.Tier ? _secondaryIndex : _primaryIndex;
            }

            await this.Rebuild(indexToRebuild);
            SetCurrentIndex(indexToRebuild);
            _isRebuilding = false;
        }

        public Task LoadLatestIndex()
        {
            //load the most recently used index
            var primaryIsValid = IsValidIndex(_primaryIndex);
            var secondaryIsValid = IsValidIndex(_secondaryIndex);

            if (primaryIsValid && secondaryIsValid)
            {
                var pf = new FileInfo(_primaryIndex.IndexRootPath);
                var sf = new FileInfo(_secondaryIndex.IndexRootPath);
                SetCurrentIndex(pf.CreationTimeUtc >= sf.CreationTimeUtc ? _primaryIndex : _secondaryIndex);
            }

            if (primaryIsValid)
            {
                SetCurrentIndex(_primaryIndex);
            }

            if (secondaryIsValid)
            {
                SetCurrentIndex(_secondaryIndex);
            }

            //neither index is valid; so let us default
            //to primary.  It will be built as files are added.
            SetCurrentIndex(_primaryIndex);

            //if there were files but neither index is valid
            //let's kick off a background rebuild on the secondary
            //index to pickup all the files
            var fileCount = this.GetFileCount().GetAwaiter().GetResult();
            if (fileCount > 0)
            {
                //let it run on its own
                this.RebuildIndex();
            }

            return Task.CompletedTask;
        }

        public Task<Int64> GetFileCount()
        {
            //TODO: This really won't perform
            var dirs = Directory.GetDirectories(_drive.GetStoragePath(StorageDisposition.LongTerm));
            return Task.FromResult(dirs.LongLength);
        }

        public StorageDriveIndex GetCurrentIndex()
        {
            return _currentIndex;
        }

        private void WriteStream(Stream stream, string filePath)
        {
            var buffer = new byte[WriteChunkSize];
            var bytesRead = 0;

            using (var output = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                do
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    output.Write(buffer, 0, bytesRead);
                } while (bytesRead > 0);

                output.Close();
            }
        }

        private void SetCurrentIndex(StorageDriveIndex index)
        {
            //TODO: do i need to lock here?
            _currentIndex = index;
            _indexReadyState = IsValidIndex(index) ? IndexReadyState.Ready : IndexReadyState.NotAvailable;
        }

        private bool IsValidIndex(StorageDriveIndex index)
        {
            //TODO: this needs more rigor than just checking the number of files
            var qFileCount = Directory.Exists(index.GetQueryIndexPath()) ? Directory.GetFiles(index.GetQueryIndexPath()).Count() : 0;
            var pFileCount = Directory.Exists(index.GetPermissionIndexPath()) ? Directory.GetFiles(index.GetPermissionIndexPath()).Count() : 0;
            return qFileCount > 0 && pFileCount > 0;
        }
    }
}