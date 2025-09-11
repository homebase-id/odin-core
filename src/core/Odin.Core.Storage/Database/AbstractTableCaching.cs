using System.Threading.Tasks;

namespace Odin.Core.Storage.Database;

#nullable enable

//
// Cache-aside/invalidate-only pattern used below.
// This means that if there is a cache miss, we go to the database,
// and we only update the cache when we have a new/updated item.
// If an item is deleted or updated, we invalidate
// (aka remove) the cache entry.
//

public abstract class AbstractTableCaching(ITransactionalCacheFactory cacheFactory, string rootTag)
{
    protected readonly TransactionalCache Cache = cacheFactory.Create(rootTag);

    public long Hits => Cache.Hits;
    public long Misses => Cache.Misses;

    public Task InvalidateAllAsync()
    {
        return Cache.InvalidateAllAsync();
    }
}
