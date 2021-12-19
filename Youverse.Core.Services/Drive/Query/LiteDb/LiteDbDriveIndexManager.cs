using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveIndexManager : IDriveIndexManager
    {
        private readonly ISystemStorage _systemStorage;
        private readonly LiteDbDriveMetadataIndexer _indexer;

        private readonly StorageDriveIndex _primaryIndex;
        private readonly StorageDriveIndex _secondaryIndex;

        private readonly IGranteeResolver _granteeResolver;
        private readonly IStorageManager _storageManager;

        private StorageDriveIndex _currentIndex;
        private bool _isRebuilding;
        private IndexReadyState _indexReadyState;

        public LiteDbDriveIndexManager(StorageDrive drive, ISystemStorage systemStorage, IProfileAttributeManagementService profileSvc, IGranteeResolver granteeResolver, IStorageManager storageManager)
        {
            _systemStorage = systemStorage;
            _granteeResolver = granteeResolver;
            _storageManager = storageManager;
            this.Drive = drive;
            
            _primaryIndex = new StorageDriveIndex(IndexTier.Primary, Drive.RootPath);
            _secondaryIndex = new StorageDriveIndex(IndexTier.Secondary, Drive.RootPath);

            //TODO: pickup here:
            _indexer = new LiteDbDriveMetadataIndexer(this.Drive, profileSvc, granteeResolver, storageManager, logger:null);
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

            _indexReadyState = IndexReadyState.NotAvailable;
        }

        private bool IsValidIndex(StorageDriveIndex index)
        {
            return File.Exists(index.GetQueryIndexPath()) && File.Exists(index.GetPermissionIndexPath());
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