using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppGrants(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableAppGrantsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public new async Task<bool> TryInsertAsync(AppGrantsRecord item)
    {
        item.identityId = identityKey;
        return await base.TryInsertAsync(item);
    }

    public new async Task<int> InsertAsync(AppGrantsRecord item)
    {
        item.identityId = identityKey;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(AppGrantsRecord item)
    {
        item.identityId = identityKey;
        return await base.UpsertAsync(item);
    }

    public async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId)
    {
        return await base.GetByOdinHashIdAsync(identityKey, odinHashId);
    }

    public async Task DeleteByIdentityAsync(Guid odinHashId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var grants = await GetByOdinHashIdAsync(identityKey, odinHashId);

        if (grants == null)
            return;

        foreach (var grant in grants)
        {
            await DeleteAsync(identityKey, odinHashId, grant.appId, grant.circleId);
        }

        tx.Commit();
    }
}
