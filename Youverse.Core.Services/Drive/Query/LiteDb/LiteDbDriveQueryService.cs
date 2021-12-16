using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Drive.Query.LiteDb
{
    public class LiteDbDriveQueryService : IDriveQueryService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveResolver _driveResolver;

        public LiteDbDriveQueryService(ISystemStorage systemStorage, IDriveResolver driveResolver)
        {
            _systemStorage = systemStorage;
            _driveResolver = driveResolver;
        }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid driveId, bool includeContent, PageOptions pageOptions)
        {
            var container = await _driveResolver.Resolve(driveId);
            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(container.IndexName, s => s.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp));
            
            if (!includeContent)
            {
                StripContent(ref page);
            }
            
            return page;
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid driveId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            var container = await _driveResolver.Resolve(driveId);
            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(container.IndexName, s => s.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions));

            if (!includeContent)
            {
                StripContent(ref page);
            }

            return page;
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
    }
}