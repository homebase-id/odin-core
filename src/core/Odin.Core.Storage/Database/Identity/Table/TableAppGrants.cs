using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppGrants(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableAppGrantsCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    internal new async Task<bool> TryInsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    internal new async Task<int> InsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<int> UpsertAsync(AppGrantsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    internal async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId)
    {
        return await base.GetByOdinHashIdAsync(odinIdentity, odinHashId);
    }

    internal async Task<List<AppGrantsRecord>> GetAllAsync()
    {
        return await base.GetAllAsync(odinIdentity);
    }


    internal async Task DeleteByIdentityAsync(Guid odinHashId)
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