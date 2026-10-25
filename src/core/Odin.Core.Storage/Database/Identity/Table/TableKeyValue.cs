using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableKeyValue(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableKeyValueCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    internal async Task<KeyValueRecord> GetAsync(byte[] key)
    {
        return await base.GetAsync(odinIdentity, key);
    }

    public async Task<List<KeyValueRecord>> GetAll()
    {
        return await base.GetAllByIdentityIdAsync(odinIdentity);
    }

    internal new async Task<int> InsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<bool> TryInsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    internal async Task<int> DeleteAsync(byte[] key)
    {
        return await base.DeleteAsync(odinIdentity, key);
    }

    internal new async Task<int> UpsertAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }

    internal async Task<int> UpsertManyAsync(List<KeyValueRecord> items)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var affectedRows = 0;
        foreach (var item in items)
        {
            item.identityId = odinIdentity;
            affectedRows += await base.UpsertAsync(item);
        }

        tx.Commit();

        return affectedRows;
    }

    internal new async Task<int> UpdateAsync(KeyValueRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpdateAsync(item);
    }

    /// <summary>
    /// A compare-and-swap: replaces <paramref name="key"/>'s data with <paramref name="data"/> only while
    /// it is still <paramref name="expectedData"/>.  False when it isn't (changed, or gone).
    /// </summary>
    internal async Task<bool> UpdateIfAsync(byte[] key, byte[] expectedData, byte[] data)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var updateCommand = cn.CreateCommand();
        updateCommand.CommandText = "UPDATE KeyValue SET data = @data " +
                                    "WHERE identityId = @identityId AND key = @key AND data = @expectedData;";
        updateCommand.AddParameter("@identityId", DbType.Binary, odinIdentity.IdentityId);
        updateCommand.AddParameter("@key", DbType.Binary, key);
        updateCommand.AddParameter("@expectedData", DbType.Binary, expectedData);
        updateCommand.AddParameter("@data", DbType.Binary, data);
        return await updateCommand.ExecuteNonQueryAsync() > 0;
    }

    public async Task<(List<KeyValueRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, odinIdentity.IdentityId, inCursor);
    }
}