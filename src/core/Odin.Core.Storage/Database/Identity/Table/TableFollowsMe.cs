﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;

//
// FollowsMe - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableFollowsMe(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableFollowsMeCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    public const int GuidSize = 16; // Precisely 16 bytes for the ID key
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<int> DeleteAsync(OdinId identity, Guid driveId)
    {
        return await base.DeleteAsync(odinIdentity, identity.DomainName, driveId);
    }

    public async Task<int> DeleteAndInsertManyAsync(OdinId identity, List<FollowsMeRecord> items)
    {
        var recordsInserted = 0;

        await DeleteByIdentityAsync(identity);

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        foreach (var item in items)
        {
            item.identityId = odinIdentity;
            recordsInserted += await base.InsertAsync(item);
        }

        tx.Commit();

        return recordsInserted;
    }


    public new async Task<int> InsertAsync(FollowsMeRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<bool> TryInsertAsync(FollowsMeRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    public new async Task<int> UpsertAsync(FollowsMeRecord item)
    {
        item.identityId = odinIdentity;
        return await base.UpsertAsync(item);
    }



    /// <summary>
    /// For the given identity, return all drives being followed (and possibly Guid.Empty for everything)
    /// </summary>
    /// <param name="identity">The identity following you</param>
    /// <returns>List of driveIds (possibly includinig Guid.Empty for 'follow all')</returns>
    /// <exception cref="Exception"></exception>
    public async Task<List<FollowsMeRecord>> GetAsync(OdinId identity)
    {
        var r = await base.GetAsync(odinIdentity, identity.DomainName) ?? [];
        return r;
    }

    public async Task<int> DeleteByIdentityAsync(OdinId identity)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();

        var records = await base.GetAsync(odinIdentity, identity.DomainName);
        if (records == null)
        {
            return 0;
        }

        await using var tx = await cn.BeginStackedTransactionAsync();

        var n = 0;
        foreach (var record in records)
        {
            n += await base.DeleteAsync(odinIdentity, identity.DomainName, record.driveId);
        }

        tx.Commit();

        return n;
    }

    // Returns # records inserted (1 or 0)
    public async Task<int> DeleteAndAddFollowerAsync(FollowsMeRecord record)
    {
        record.identityId = odinIdentity;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        var followerList = await base.GetAsync(odinIdentity, record.identity);
        foreach (var follower in followerList)
        {
            await base.DeleteAsync(odinIdentity, follower.identity, follower.driveId);
        }
        var n = await base.InsertAsync(record);

        tx.Commit();

        return n;
    }

    /// <summary>
    /// Return pages of identities that follow me; up to count size.
    /// Optionally supply a cursor to indicate the last identity processed (sorted ascending)
    /// </summary>
    /// <param name="count">Maximum number of identities per page</param>
    /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
    /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(List<string> followers, string nextCursor)> GetAllFollowersAsync(int count, string inCursor)
    {
        if (count < 1)
            throw new Exception("Count must be at least 1.");

        if (count == int.MaxValue)
            count--; // avoid overflow when doing +1 on the param below

        if (inCursor == null)
            inCursor = "";

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT DISTINCT identity FROM followsme WHERE identityId = @identityId AND identity > @cursor ORDER BY identity ASC LIMIT @count;";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@cursor";
        param2.ParameterName = "@count";
        param3.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = inCursor;
        param2.Value = count + 1;
        param3.Value = odinIdentity.IdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var result = new List<string>();
            string nextCursor;

            int n = 0;

            while ((n < count) && await rdr.ReadAsync())
            {
                n++;
                var s = (string) rdr[0];
                if (s.Length < 1)
                    throw new Exception("Empty string");
                result.Add(s);
            }

            if ((n > 0) && rdr.HasRows)
            {
                nextCursor = result[n - 1];
            }
            else
            {
                nextCursor = null;
            }

            return (result, nextCursor);
        }
    }

    /// <summary>
    /// Return pages of identities, following driveId, up to count size.
    /// Optionally supply a cursor to indicate the last identity processed (sorted ascending)
    /// </summary>
    /// <param name="count">Maximum number of identities per page</param>
    /// <param name="driveId">The drive they're following that you want to get a list for</param>
    /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
    /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(List<string> followers, string nextCursor)> GetFollowersAsync(int count, Guid driveId, string inCursor)
    {
        var databaseType = _scopedConnectionFactory.DatabaseType;

        if (count < 1)
            throw new Exception("Count must be at least 1.");

        if (count == int.MaxValue)
            count--; // avoid overflow when doing +1 on the param below

        if (inCursor == null)
            inCursor = "";

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT DISTINCT identity FROM followsme WHERE identityId=@identityId AND (driveId=@driveId OR driveId = {Guid.Empty.BytesToSql(databaseType)}) AND identity > @cursor ORDER BY identity ASC LIMIT @count;";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();
        var param4 = cmd.CreateParameter();

        param1.ParameterName = "@driveId";
        param2.ParameterName = "@cursor";
        param3.ParameterName = "@count";
        param4.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);
        cmd.Parameters.Add(param4);

        param1.Value = driveId.ToByteArray();
        param2.Value = inCursor;
        param3.Value = count + 1;
        param4.Value = odinIdentity.IdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var result = new List<string>();
            string nextCursor;

            int n = 0;

            while ((n < count) && await rdr.ReadAsync())
            {
                n++;
                var s = (string) rdr[0];
                if (s.Length < 1)
                    throw new Exception("Empty string");
                result.Add(s);
            }

            if ((n > 0) && rdr.Read())
            {
                nextCursor = result[n - 1];
            }
            else
            {
                nextCursor = null;
            }

            return (result, nextCursor);
        }
    }
}