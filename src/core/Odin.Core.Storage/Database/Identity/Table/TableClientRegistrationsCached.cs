using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.Database.Identity.Table;

#nullable enable

public class TableClientRegistrationsCached(TableClientRegistrations table, IIdentityTransactionalCacheFactory cacheFactory) :
    AbstractTableCaching(cacheFactory, table.GetType().Name, table.GetType().Name)
{
    private static readonly List<string> PagingByClientRegistrationIdTags = ["PagingByClientRegistrationIdTags"];

    //

    private static string GetCacheKey(ClientRegistrationsRecord item)
    {
        return GetCacheKey(item.catId);
    }

    //

    private static string GetCacheKey(Guid catId)
    {
        return catId.ToString();
    }

    //

    private Task InvalidateAsync(ClientRegistrationsRecord item)
    {
        return InvalidateAsync(item.catId);
    }

    //

    private Task InvalidateAsync(Guid catId)
    {
        return Cache.InvalidateAsync([
            Cache.CreateRemoveByKeyAction(GetCacheKey(catId)),
            Cache.CreateRemoveByTagsAction(PagingByClientRegistrationIdTags)
        ]);
    }

    //

    public async Task<ClientRegistrationsRecord?> GetAsync(Guid catId, TimeSpan? ttl = null)
    {
        var result = await Cache.GetOrSetAsync(
            GetCacheKey(catId),
            _ => table.GetAsync(catId),
            ttl ?? DefaultTtl);
        return result;
    }

    //

    public async Task<int> InsertAsync(ClientRegistrationsRecord item)
    {
        var result = await table.InsertAsync(item);

        await InvalidateAsync(item);

        return result;
    }

    //

    public async Task<int> DeleteAsync(Guid catId)
    {
        var result = await table.DeleteAsync(catId);

        await InvalidateAsync(catId);

        return result;
    }

    //

    public async Task<int> UpsertAsync(ClientRegistrationsRecord item)
    {
        var result = await table.UpsertAsync(item);
        await InvalidateAsync(item);
        return result;
    }
}