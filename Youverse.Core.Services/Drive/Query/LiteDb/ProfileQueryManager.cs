using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive.Security;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Profile;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class ProfileQueryManager : IDriveQueryManager
    {
        private StorageDriveIndex _currentIndex;
        private IndexReadyState _indexReadyState;

        private ILogger<object> _logger;
        public static readonly Guid DataAttributeDriveId = Guid.Parse("11111234-2931-4fa1-0000-CCCC40000001");

        public ProfileQueryManager(StorageDrive drive, ISystemStorage systemStorage, IProfileAttributeManagementService profileSvc, IGranteeResolver granteeResolver, ILogger<object> logger)
        {
            _logger = logger;
            this.Drive = drive;
        }

        public IndexReadyState IndexReadyState => _indexReadyState;

        public StorageDrive Drive { get; init; }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(bool includeContent, PageOptions pageOptions)
        {
            AssertValidIndexLoaded();

            using var indexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, _currentIndex.GetQueryIndexPath(), _currentIndex.QueryIndexName);
            var page = await indexStorage.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp);

            //var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(_currentIndex.QueryIndexName, s => s.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp));

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

            // var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(_currentIndex.QueryIndexName, s => s.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions));
            using var indexStorage = new LiteDBSingleCollectionStorage<IndexedItem>(_logger, _currentIndex.GetQueryIndexPath(), _currentIndex.QueryIndexName);
            var page = await indexStorage.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions);

            if (!includeContent)
            {
                StripContent(ref page);
            }

            return page;
        }

        public void UpdateIndex(DriveFileId file, FileMetaData metadata)
        {
            throw new NotImplementedException();
        }

        public Task SetCurrentIndex(StorageDriveIndex index)
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

            return Task.CompletedTask;
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

        private void AssertValidIndexLoaded()
        {
            if (_indexReadyState != IndexReadyState.Ready)
            {
                throw new NoValidIndexException(this.Drive.Id);
            }
        }
    }
}