using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity;

public class TableAppGrants(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppGrantsCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public new async Task<bool> TryInsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    public new async Task<int> InsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    public async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId)
    {
        return await base.GetByOdinHashIdAsync(odinIdentity, odinHashId);
    }

    public async Task<List<AppGrantsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }


    public async Task DeleteByIdentityAsync(Guid odinHashId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var grants = await GetByOdinHashIdAsync(odinIdentity, odinHashId);

        if (grants == null)
            return;

        foreach (var grant in grants)
        {
            await DeleteAsync(odinIdentity, odinHashId, grant.appId, grant.circleId);
        }

        tx.Commit();
    }
}