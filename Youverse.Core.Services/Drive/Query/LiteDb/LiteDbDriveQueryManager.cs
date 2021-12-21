using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveQueryManager : IDriveQueryManager
    {
        private readonly ISystemStorage _systemStorage;

        private StorageDriveIndex _currentIndex;
        private IndexReadyState _indexReadyState;

        public LiteDbDriveQueryManager(StorageDrive drive, ISystemStorage systemStorage)
        {
            _systemStorage = systemStorage;
            this.Drive = drive;
        }

        public IndexReadyState IndexReadyState => _indexReadyState;

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

        public void UpdateIndex(DriveFileId file, MetadataIndexDefinition metadata)
        {

            var item = new IndexedItem()
            {
                Id = Guid.NewGuid(),
                FileId = file.FileId,
                CreatedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                LastUpdatedTimestamp = DateTimeExtensions.UnixTimeMilliseconds(),
                CategoryId = Guid.Empty,
            };

            _systemStorage.WithTenantSystemStorage<IndexedItem>(_currentIndex.QueryIndexName, s => s.Save(item));
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