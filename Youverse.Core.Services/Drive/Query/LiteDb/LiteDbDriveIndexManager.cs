using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveIndexManager : IDriveIndexManager
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveMetadataIndexer _indexer;

        private readonly StorageDriveIndex _primaryIndex;
        private readonly StorageDriveIndex _secondaryIndex;

        private readonly IGranteeResolver _granteeResolver;
        private readonly IStorageManager _storageManager;

        private StorageDriveIndex _currentIndex;
        private bool _isRebuilding;
        private IndexReadyState _indexReadyState;

        private readonly ILogger<object> _logger;

        public LiteDbDriveIndexManager(StorageDrive drive, ISystemStorage systemStorage, IGranteeResolver granteeResolver, IStorageManager storageManager, ILogger<object> logger)
        {
            _systemStorage = systemStorage;
            _granteeResolver = granteeResolver;
            _storageManager = storageManager;
            _logger = logger;
            this.Drive = drive;

            _primaryIndex = new StorageDriveIndex(IndexTier.Primary, Drive.LongTermDataRootPath);
            _secondaryIndex = new StorageDriveIndex(IndexTier.Secondary, Drive.LongTermDataRootPath);

            _indexer = new LiteDbDriveMetadataIndexer(this.Drive, granteeResolver, storageManager, _logger);
        }

        public IndexReadyState IndexReadyState => _indexReadyState;

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

            return Task.CompletedTask;
        }

        public StorageDrive Drive { get; init; }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(bool includeContent, PageOptions pageOptions)
        {
            AssertValidIndexLoaded();

            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(_currentIndex.QueryIndexName, s => s.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp));

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

            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(_currentIndex.QueryIndexName, s => s.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions));

            if (!includeContent)
            {
                StripContent(ref page);
            }

            return page;
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
                indexToRebuild = _currentIndex.IndexTier == _primaryIndex.IndexTier ? _secondaryIndex : _primaryIndex;
            }

            await _indexer.Rebuild(indexToRebuild);
            SetCurrentIndex(indexToRebuild);
            _isRebuilding = false;
        }

        private void SetCurrentIndex(StorageDriveIndex index)
        {
            if (IsValidIndex(index))
            {
                //TODO: do i need to lock here?
                _currentIndex = index;
                _indexReadyState = IndexReadyState.Ready;
            }
            else
            {
                _indexReadyState = IndexReadyState.NotAvailable;
            }
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
                indexedItem.JsonPayload = string.Empty;
            }
        }

        private void AssertValidIndexLoaded()
        {
            if (_indexReadyState != IndexReadyState.Ready)
            {
                throw new NoValidIndexException(this.Drive.Id);
            }
        }
    }
}