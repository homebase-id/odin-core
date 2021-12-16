using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Container.Query.LiteDb
{
    public class LiteDbContainerQueryService : IContainerQueryService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IContainerResolver _containerResolver;

        public LiteDbContainerQueryService(ISystemStorage systemStorage, IContainerResolver containerResolver)
        {
            _systemStorage = systemStorage;
            _containerResolver = containerResolver;
        }

        public async Task<PagedResult<IndexedItem>> GetRecentlyCreatedItems(Guid containerId, bool includeContent, PageOptions pageOptions)
        {
            var container = await _containerResolver.Resolve(containerId);
            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(container.IndexName, s => s.GetList(pageOptions, ListSortDirection.Descending, item => item.CreatedTimestamp));
            return page;
        }

        public async Task<PagedResult<IndexedItem>> GetItemsByCategory(Guid containerId, Guid categoryId, bool includeContent, PageOptions pageOptions)
        {
            var container = await _containerResolver.Resolve(containerId);
            var page = await _systemStorage.WithTenantSystemStorageReturnList<IndexedItem>(container.IndexName, s => s.Find(item => item.CategoryId == categoryId, ListSortDirection.Descending, item => item.CreatedTimestamp, pageOptions));
            return page;
        }
    }
}