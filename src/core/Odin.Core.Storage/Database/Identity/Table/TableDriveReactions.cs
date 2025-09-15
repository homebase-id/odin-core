using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableDriveReactions(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity)
    : TableDriveReactionsCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<int> DeleteAsync(Guid driveId, OdinId identity, Guid postId, string singleReaction)
    {
        return await base.DeleteAsync(odinIdentity, driveId, postId, identity, singleReaction);
    }

    public async Task<int> DeleteAllReactionsAsync(Guid driveId, OdinId identity, Guid postId)
    {
        return await base.DeleteAllReactionsAsync(odinIdentity, driveId, identity, postId);
    }

    public new async Task<int> InsertAsync(DriveReactionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.InsertAsync(item);
    }

    public new async Task<bool> TryInsertAsync(DriveReactionsRecord item)
    {
        item.identityId = odinIdentity;
        return await base.TryInsertAsync(item);
    }

    public async Task<(List<string>, int)> GetPostReactionsAsync(Guid driveId, Guid postId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=@identityId AND driveId=@driveId AND postId=@postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

        var sparam1 = cmd.CreateParameter();
        var sparam2 = cmd.CreateParameter();
        var sparam3 = cmd.CreateParameter();

        sparam1.ParameterName = "@postId";
        sparam2.ParameterName = "@driveId";
        sparam3.ParameterName = "@identityId";

        cmd.Parameters.Add(sparam1);
        cmd.Parameters.Add(sparam2);
        cmd.Parameters.Add(sparam3);

        sparam1.Value = postId.ToByteArray();
        sparam2.Value = driveId.ToByteArray();
        sparam3.Value = odinIdentity.IdentityIdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var result = new List<string>();
            int totalCount = 0;
            int n = 0;

            while (await rdr.ReadAsync())
            {
                // Only return the first five reactions (?)
                if (n < 5)
                {
                    string s = (string) rdr[0];
                    result.Add(s);
                }

                int count = (int)(long) rdr[1];
                totalCount += count;
                n++;
            }

            return (result, totalCount);
        }
    }

    /// <summary>
    /// Get the number of reactions  made by the identity on a given post.
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="postId"></param>
    /// <returns></returns>
    public async Task<int> GetIdentityPostReactionsAsync(OdinId identity, Guid driveId, Guid postId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=@identityId AND identity=@identity AND postId=@postId AND driveId = @driveId;";

        var s2param1 = cmd.CreateParameter();
        var s2param2 = cmd.CreateParameter();
        var s2param3 = cmd.CreateParameter();
        var s2param4 = cmd.CreateParameter();

        s2param1.ParameterName = "@postId";
        s2param2.ParameterName = "@identity";
        s2param3.ParameterName = "@driveId";
        s2param4.ParameterName = "@identityId";

        cmd.Parameters.Add(s2param1);
        cmd.Parameters.Add(s2param2);
        cmd.Parameters.Add(s2param3);
        cmd.Parameters.Add(s2param4);

        s2param1.Value = postId.ToByteArray();
        s2param2.Value = identity.DomainName;
        s2param3.Value = driveId.ToByteArray();
        s2param4.Value = odinIdentity.IdentityIdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
                return (int)(long) rdr[0];
            else
                return 0;
        }
    }


    /// <summary>
    /// Get the number of reactions  made by the identity on a given post.
    /// </summary>
    /// <param name="identity"></param>
    /// <param name="postId"></param>
    /// <returns></returns>
    public async Task<List<string>> GetIdentityPostReactionDetailsAsync(OdinId identity, Guid driveId, Guid postId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT singleReaction as reactioncount FROM driveReactions WHERE identityId=@identityId AND identity=@identity AND postId=@postId AND driveId = @driveId;";

        var s3param1 = cmd.CreateParameter();
        var s3param2 = cmd.CreateParameter();
        var s3param3 = cmd.CreateParameter();
        var s3param4 = cmd.CreateParameter();

        s3param1.ParameterName = "@postId";
        s3param2.ParameterName = "@identity";
        s3param3.ParameterName = "@driveId";
        s3param4.ParameterName = "@identityId";

        cmd.Parameters.Add(s3param1);
        cmd.Parameters.Add(s3param2);
        cmd.Parameters.Add(s3param3);
        cmd.Parameters.Add(s3param4);

        s3param1.Value = postId.ToByteArray();
        s3param2.Value = identity.DomainName;
        s3param3.Value = driveId.ToByteArray();
        s3param4.Value = odinIdentity.IdentityIdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var rs = new List<string>();

            while (await rdr.ReadAsync())
            {
                string s = (string) rdr[0];
                rs.Add(s);
            }

            return rs;
        }
    }


    public async Task<(List<string>, List<int>, int)> GetPostReactionsWithDetailsAsync(Guid driveId, Guid postId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            $"SELECT singleReaction, COUNT(singleReaction) as reactioncount FROM driveReactions WHERE identityId=@identityId AND driveId=@driveId AND postId=@postId GROUP BY singleReaction ORDER BY reactioncount DESC;";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@postId";
        param2.ParameterName = "@driveId";
        param3.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = postId.ToByteArray();
        param2.Value = driveId.ToByteArray();
        param3.Value = odinIdentity.IdentityIdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var result = new List<string>();
            var iresult = new List<int>();
            int totalCount = 0;

            while (rdr.Read())
            {
                string s = (string) rdr[0];
                result.Add(s);

                int count = (int)(Int64) rdr[1];
                iresult.Add(count);

                totalCount += count;
            }

            return (result, iresult, totalCount);
        }
    }


    // Copied and modified from CRUD
    public async Task<(List<DriveReactionsRecord>, Int32? nextCursor)> PagingByRowidAsync(int count, Int32? inCursor, Guid driveId, Guid postIdFilter)
    {
        if (count < 1)
            throw new Exception("Count must be at least 1.");

        if (count == int.MaxValue)
            count--; // avoid overflow when doing +1 on the param below

        if (inCursor == null)
            inCursor = 0;

        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "SELECT rowid,identity,postId,singleReaction FROM driveReactions " +
                          "WHERE identityId=@identityId AND driveId = @driveId AND postId = @postId AND rowid > @rowid ORDER BY rowid ASC LIMIT @count;";

        var getPaging0Param1 = cmd.CreateParameter();
        var getPaging0Param2 = cmd.CreateParameter();
        var getPaging0Param3 = cmd.CreateParameter();
        var getPaging0Param4 = cmd.CreateParameter();
        var getPaging0Param5 = cmd.CreateParameter();

        getPaging0Param1.ParameterName = "@rowid";
        getPaging0Param2.ParameterName = "@count";
        getPaging0Param3.ParameterName = "@postId";
        getPaging0Param4.ParameterName = "@driveId";
        getPaging0Param5.ParameterName = "@identityId";

        cmd.Parameters.Add(getPaging0Param1);
        cmd.Parameters.Add(getPaging0Param2);
        cmd.Parameters.Add(getPaging0Param3);
        cmd.Parameters.Add(getPaging0Param4);
        cmd.Parameters.Add(getPaging0Param5);

        getPaging0Param1.Value = inCursor;
        getPaging0Param2.Value = count + 1;
        getPaging0Param3.Value = postIdFilter.ToByteArray();
        getPaging0Param4.Value = driveId.ToByteArray();
        getPaging0Param5.Value = odinIdentity.IdentityIdAsByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            var result = new List<DriveReactionsRecord>();
            Int32? nextCursor;

            int n = 0;
            int rowid = 0;
            while ((n < count) && await rdr.ReadAsync())
            {
                n++;
                var item = new DriveReactionsRecord();
                byte[] _tmpbuf = new byte[65535 + 1];

                item.identityId = odinIdentity;

                rowid = (int)(Int64) rdr[0];

                if ((rdr[1] == DBNull.Value))
                    throw new Exception("Impossible, item is null in but set as NOT NULL");
                else
                {
                    var s = (string) rdr[1];
                    item.identity = new OdinId(s);
                }

                if ((rdr[2] == DBNull.Value))
                    throw new Exception("Impossible, item is null in but set as NOT NULL");
                else
                {
                    var _guid = (byte[])rdr[2];
                    if (_guid.Length != 16)
                        throw new Exception("Not a GUID in postId...");
                    item.postId = new Guid(_guid);
                }

                if ((rdr[3] == DBNull.Value))
                    throw new Exception("Impossible, item is null in but set as NOT NULL");
                else
                {
                    item.singleReaction = (string) rdr[3];
                }

                result.Add(item);
            } // while
            if ((n > 0) && await rdr.ReadAsync())
            {
                nextCursor = rowid;
            }
            else
            {
                nextCursor = null;
            }

            return (result, nextCursor);
        } // using
    }
}