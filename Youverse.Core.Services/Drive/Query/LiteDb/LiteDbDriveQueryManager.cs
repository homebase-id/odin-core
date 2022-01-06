using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveQueryManager : IDriveQueryManager
    {
        private LiteDBSingleCollectionStorage<IndexedItem> _indexStorage;
        private LiteDBSingleCollectionStorage<IndexedItem> _backupIndexStorage;
        private StorageDriveIndex _currentIndex;
        private IndexReadyState _indexReadyState;
        private readonly ILogger<object> _logger;

        private readonly StorageDriveIndex _primaryIndex;
        private readonly StorageDriveIndex _secondaryIndex;

        private readonly string IndexCollectionName = "index";

        public LiteDbDriveQueryManager(StorageDrive drive, ILogger<object> logger)
        {
            _logger = logger;
            this.Drive = drive;

            _primaryIndex = new StorageDriveIndex(IndexTier.Primary, drive.GetIndexPath());
            _secondaryIndex = new StorageDriveIndex(IndexTier.Secondary, drive.GetIndexPath());
        }

        public IndexReadyState IndexReadyState
        {
            get { return this._indexReadyState; }
            set { this._indexReadyState = value; }
        }

        public StorageDrive Drive { get; init; }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(bool includeContent, PageOptions pageOptions)
        {
            AssertValidIndexLoaded();
            var page = await _indexStorage.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp);

            //apply permissions from the permissions index to reduce the set.
            //

            if (!includeContent)
            {
                StripContent(ref page);
            }

            return page;
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            AssertValidIndexLoaded();

            var page = await _indexStorage.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions);

            if (!includeContent)
            {
                StripContent(ref page);
            }

            return page;
        }

        public Task SwitchIndex()
        {
            //TODO: do i need to lock here?
            var index = _currentIndex.Tier == IndexTier.Primary ? _secondaryIndex : _primaryIndex;
            SetCurrentIndex(index);
            return Task.CompletedTask;
        }

        public Task UpdateCurrentIndex(FileMetadata metadata)
        {
            _indexStorage.Save(ConvertMetadata(metadata));

            //technically the index is ready because it has at least one item in it
            //this, however, means we need to build a way for apps to understand the
            //actual state of the index so they can notify users
            _indexReadyState = IndexReadyState.Ready;

            return Task.CompletedTask;
        }

        public Task UpdateSecondaryIndex(FileMetadata metadata)
        {
            if (null == _backupIndexStorage)
            {
                throw new Exception("Backup index not ready; call PrepareBackupIndexForRebuild before calling UpdateBackupIndex");
            }

            _backupIndexStorage.Save(ConvertMetadata(metadata));

            return Task.CompletedTask;
        }

        public Task PrepareSecondaryIndexForRebuild()
        {
            var backupIndex = _currentIndex.Tier == IndexTier.Primary ? _secondaryIndex : _primaryIndex;
            var indexPath = backupIndex.GetQueryIndexPath();
            if (null != _backupIndexStorage)
            {
                _backupIndexStorage.Dispose();
                _backupIndexStorage = null;
            }

            if (Directory.Exists(indexPath))
            {
                Directory.Delete(indexPath, true);
            }

            _backupIndexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, indexPath, IndexCollectionName);

            return Task.CompletedTask;
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
                _indexReadyState = IndexReadyState.Ready;
            }

            if (primaryIsValid)
            {
                SetCurrentIndex(_primaryIndex);
                _indexReadyState = IndexReadyState.Ready;
            }

            if (secondaryIsValid)
            {
                SetCurrentIndex(_secondaryIndex);
                _indexReadyState = IndexReadyState.Ready;
            }

            //neither index is valid; so let us default
            //to primary.  It will be built as files are added.
            SetCurrentIndex(_primaryIndex);
            _indexReadyState = IndexReadyState.RequiresRebuild;

            return Task.CompletedTask;
        }

        private void SetCurrentIndex(StorageDriveIndex index)
        {
            _currentIndex = index;
            if (null != _indexStorage)
            {
                _indexStorage.Dispose();
            }

            var indexPath = _currentIndex.GetQueryIndexPath();
            _indexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, indexPath, IndexCollectionName);
            _indexReadyState = IndexReadyState.Ready;
        }

        private bool IsValidIndex(StorageDriveIndex index)
        {
            //TODO: this needs more rigor than just checking the number of files
            var qFileCount = Directory.Exists(index.GetQueryIndexPath()) ? Directory.GetFiles(index.GetQueryIndexPath()).Count() : 0;
            var pFileCount = Directory.Exists(index.GetPermissionIndexPath()) ? Directory.GetFiles(index.GetPermissionIndexPath()).Count() : 0;
            return qFileCount > 0 && pFileCount > 0;
        }

        private void StripContent(ref PagedResult<IndexedItem> page)
        {
            //Note: I'm not fond of this method but it works with litedb; since litedb, in our case, just returns the whole object.
            //the alternative is casting but I don't think that's needed right now
            foreach (var indexedItem in page.Results)
            {
                indexedItem.JsonContent = string.Empty;
            }
        }

        private IndexedItem ConvertMetadata(FileMetadata metadata)
        {
            return new IndexedItem()
            {
                FileId = metadata.File.FileId,
                CreatedTimestamp = metadata.Created,
                LastUpdatedTimestamp = metadata.Updated,
                CategoryId = metadata.AppData.CategoryId,
                ContentIsComplete = metadata.AppData.ContentIsComplete,
                JsonContent = metadata.AppData.JsonContent
            };
        }
        
        private void AssertValidIndexLoaded()
        {
            if (_indexReadyState == IndexReadyState.Ready)
            {
                return;
            }

            //keep checking in-case a background indexing makes the index available
            _indexReadyState = IsValidIndex(_currentIndex) ? IndexReadyState.Ready : IndexReadyState.RequiresRebuild;
            if (_indexReadyState != IndexReadyState.Ready)
            {
                throw new NoValidIndexException(this.Drive.Id);
            }
        }
    }
}