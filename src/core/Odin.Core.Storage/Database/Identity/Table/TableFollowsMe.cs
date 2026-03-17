using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

//
// FollowsMe - this class stores all the people that follow me.
// I.e. the people I need to notify when I update some content.
//

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableFollowsMe(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableFollowsMeCRUD(scopedConnectionFactory)
{
    internal const int GuidSize = 16; // Precisely 16 bytes for the ID key
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    // ChannelDriveType = SystemDriveConstants.ChannelDriveType
    private static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");

    internal async Task<int> DeleteAndInsertManyAsync(OdinId identity, List<FollowsMeRecord> items)
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


    internal new async Task<int> InsertAsync(FollowsMeRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    internal new async Task<bool> TryInsertAsync(FollowsMeRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    internal new async Task<int> UpsertAsync(FollowsMeRecord item)
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
    internal async Task<List<FollowsMeRecord>> GetAsync(OdinId identity)
    {
        var r = await base.GetAsync(odinIdentity, identity) ?? [];
        return r;
    }

    internal async Task<int> DeleteByIdentityAsync(OdinId identity)
    {
        return await base.DeleteBySubscriberOdinIdAsync(odinIdentity, identity);
    }

    // Returns # records inserted (1 or 0)
    internal async Task<int> DeleteAndAddFollowerAsync(FollowsMeRecord record)
    {
        record.identityId = odinIdentity;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();

        await base.DeleteBySubscriberOdinIdAsync(odinIdentity, record.subscriberOdinId);
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
    internal async Task<(List<string> followers, string nextCursor)> GetAllFollowersAsync(int count, string inCursor)
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
            $"SELECT DISTINCT subscriberOdinId FROM FollowsMe WHERE identityId = @identityId AND subscriberOdinId > @cursor ORDER BY subscriberOdinId ASC LIMIT @count;";

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
        param3.Value = odinIdentity.IdentityIdAsByteArray();

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
    /// <param name="driveId">The drive they're following that you want to get a list for.
    /// Use Guid.Empty to get followers who follow all notifications (sourceDriveTypeId = ChannelDriveType).</param>
    /// <param name="inCursor">If supplied then pick the next page after the supplied identity.</param>
    /// <returns>A sorted list of identities. If list size is smaller than count then you're finished</returns>
    /// <exception cref="Exception"></exception>
    internal async Task<(List<string> followers, string nextCursor)> GetFollowersAsync(int count, Guid driveId, string inCursor)
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

        string driveCondition;
        if (driveId == Guid.Empty)
        {
            // AllNotifications: match rows that follow by channel type
            driveCondition = $"sourceDriveTypeId = {ChannelDriveType.BytesToSql(databaseType)}";
        }
        else
        {
            // SelectedChannels: match specific drive OR all-channel-type followers
            driveCondition = $"(sourceDriveId = @driveId OR sourceDriveTypeId = {ChannelDriveType.BytesToSql(databaseType)})";
        }

        cmd.CommandText =
            $"SELECT DISTINCT subscriberOdinId FROM FollowsMe WHERE identityId=@identityId AND {driveCondition} AND subscriberOdinId > @cursor ORDER BY subscriberOdinId ASC LIMIT @count;";

        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();
        var param4 = cmd.CreateParameter();

        param2.ParameterName = "@cursor";
        param3.ParameterName = "@count";
        param4.ParameterName = "@identityId";

        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);
        cmd.Parameters.Add(param4);

        if (driveId != Guid.Empty)
        {
            var param1 = cmd.CreateParameter();
            param1.ParameterName = "@driveId";
            param1.Value = driveId.ToByteArray();
            cmd.Parameters.Add(param1);
        }

        param2.Value = inCursor;
        param3.Value = count + 1;
        param4.Value = odinIdentity.IdentityIdAsByteArray();

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
