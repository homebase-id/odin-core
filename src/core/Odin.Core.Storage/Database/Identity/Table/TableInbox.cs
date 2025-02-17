using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableInbox(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey)
    : TableInboxCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<InboxRecord> GetAsync(Guid fileId)
    {
        return await base.GetAsync(identityKey, fileId);
    }

    public new async Task<int> InsertAsync(InboxRecord item)
    {
        item.identityId = identityKey;

        if (item.timeStamp.milliseconds == 0)
            item.timeStamp = UnixTimeUtc.Now();

        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(InboxRecord item)
    {
        item.identityId = identityKey;

        if (item.timeStamp.milliseconds == 0)
            item.timeStamp = UnixTimeUtc.Now();

        return await base.UpsertAsync(item);
    }


    /// <summary>
    /// Pops 'count' items from the table. The items remain in the DB with the 'popstamp' unique identifier.
    /// Popstamp is used by the caller to release the items when they have been successfully processed, or
    /// to cancel the transaction and restore the items to the inbox.
    /// </summary
    /// <param name="boxId">Is the box to pop from, e.g. Drive A, or App B</param>
    /// <param name="count">How many items to 'pop' (reserve)</param>
    /// <param name="popStamp">The unique identifier for the items reserved for pop</param>
    /// <returns>List of records</returns>
    public async Task<List<InboxRecord>> PopSpecificBoxAsync(Guid boxId, int count)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "UPDATE inbox SET popstamp=@popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE identityId=@identityId AND boxId=@boxId AND popstamp IS NULL ORDER BY rowId ASC LIMIT @count); " +
            "SELECT identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified FROM inbox WHERE identityId = @identityId AND popstamp=@popstamp ORDER BY rowId ASC";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();
        var param4 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@count";
        param3.ParameterName = "@boxId";
        param4.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);
        cmd.Parameters.Add(param4);

        param1.Value = SequentialGuid.CreateGuid().ToByteArray();
        param2.Value = count;
        param3.Value = boxId.ToByteArray();
        param4.Value = identityKey.ToByteArray();

        var result = new List<InboxRecord>();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            while (await rdr.ReadAsync())
            {
                result.Add(ReadRecordFromReaderAll(rdr));
            }
        }

        tx.Commit();

        return result;
    }


    /// <summary>
    /// Status on the box
    /// </summary>
    /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(int, int, UnixTimeUtc)> PopStatusAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "SELECT count(*) FROM inbox WHERE identityId=@identityId;" +
            "SELECT count(*) FROM inbox WHERE identityId=@identityId AND popstamp IS NOT NULL;" +
            "SELECT popstamp FROM inbox WHERE identityId=@identityId AND popstamp IS NOT NULL ORDER BY popstamp DESC LIMIT 1;";

        var param1 = cmd.CreateParameter();
        param1.ParameterName = "@identityId";
        cmd.Parameters.Add(param1);
        param1.Value = identityKey.ToByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            // Read the total count
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");
            int totalCount = 0;
            if (!(rdr[0] == DBNull.Value))
                totalCount = (int)(Int64) rdr[0];

            // Read the popped count
            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            int poppedCount = 0;
            if (!(rdr[0] == DBNull.Value))
                poppedCount = (int)(Int64) rdr[0];

            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");

            var utc = UnixTimeUtc.ZeroTime;
            if (await rdr.ReadAsync())
            {
                if (!(rdr[0] == DBNull.Value))
                {
                    var bytes = (byte[]) rdr[0];
                    if (bytes.Length != 16)
                        throw new Exception("Invalid stamp");

                    var guid = new Guid(bytes);
                    utc = SequentialGuid.ToUnixTimeUtc(guid);
                }
            }

            return (totalCount, poppedCount, utc);
        }
    }


    /// <summary>
    /// Status on the box
    /// </summary>
    /// <returns>Number of total items in box, number of popped items, the oldest popped item (ZeroTime if none)</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(int totalCount, int poppedCount, UnixTimeUtc oldestItemTime)> PopStatusSpecificBoxAsync(Guid boxId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "SELECT count(*) FROM inbox WHERE identityId=@identityId AND boxId=@boxId;" +
            "SELECT count(*) FROM inbox WHERE identityId=@identityId AND boxId=@boxId AND popstamp IS NOT NULL;" +
            "SELECT popstamp FROM inbox WHERE identityId=@identityId AND boxId=@boxId ORDER BY popstamp DESC LIMIT 1;";
        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@boxId";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = boxId.ToByteArray();
        param2.Value = identityKey.ToByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            // Read the total count
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            int totalCount = 0;
            if (!(rdr[0] == DBNull.Value))
                totalCount = (int)(Int64) rdr[0];

            // Read the popped count
            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            int poppedCount = 0;
            if (!(rdr[0] == DBNull.Value))
                poppedCount = (int)(Int64) rdr[0];

            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");

            var utc = UnixTimeUtc.ZeroTime;

            // Read the marker, if any
            if (await rdr.ReadAsync())
            {
                if (!(rdr[0] == DBNull.Value))
                {
                    var bytes = (byte[]) rdr[0];
                    if (bytes.Length != 16)
                        throw new Exception("Invalid stamp");

                    var guid = new Guid(bytes);
                    utc = SequentialGuid.ToUnixTimeUtc(guid);
                }
            }
            return (totalCount, poppedCount, utc);
        }
    }



    /// <summary>
    /// Cancels the pop of items with the 'popstamp' from a previous pop operation
    /// </summary>
    /// <param name="popstamp"></param>
    public async Task<int> PopCancelAllAsync(Guid popstamp)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=@identityId AND popstamp=@popstamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = popstamp.ToByteArray();
        param2.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task PopCancelListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=@identityId AND fileid=@fileid AND popstamp=@popstamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@fileid";
        param3.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = popstamp.ToByteArray();
        param3.Value = identityKey.ToByteArray();

        foreach (var id in listFileId)
        {
            param2.Value = id.ToByteArray();
            await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }


    /// <summary>
    /// Commits (removes) the items previously popped with the supplied 'popstamp'
    /// </summary>
    /// <param name="popstamp"></param>
    public async Task<int> PopCommitAllAsync(Guid popstamp)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "DELETE FROM inbox WHERE identityId=@identityId AND popstamp=@popstamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = popstamp.ToByteArray();
        param2.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// Commits (removes) the items previously popped with the supplied 'popstamp'
    /// </summary>
    /// <param name="popstamp"></param>
    public async Task PopCommitListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var tx = await cn.BeginStackedTransactionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "DELETE FROM inbox WHERE identityId=@identityId AND fileid=@fileid AND popstamp=@popstamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@fileid";
        param3.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = popstamp.ToByteArray();
        param3.Value = identityKey.ToByteArray();

        // I'd rather not do a TEXT statement, this seems safer but slower.
        foreach (var id in listFileId)
        {
            param2.Value = id.ToByteArray();
            await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }


    /// <summary>
    /// Recover popped items older than the supplied UnixTime in seconds.
    /// This is how to recover popped items that were never processed for example on a server crash.
    /// Call with e.g. a time of more than 5 minutes ago.
    /// </summary>
    public async Task<int> PopRecoverDeadAsync(UnixTimeUtc ut)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "UPDATE inbox SET popstamp=NULL WHERE identityId=@identityId AND popstamp < @popstamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@popstamp";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = SequentialGuid.CreateGuid(new UnixTimeUtc(ut)).ToByteArray(); // UnixTimeMilliseconds
        param2.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }
}