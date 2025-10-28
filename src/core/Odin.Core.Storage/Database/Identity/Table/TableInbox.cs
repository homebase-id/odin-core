using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

[assembly: InternalsVisibleTo("Odin.Services.Drives.DriveCore.Storage")]

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableInbox(
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    OdinIdentity odinIdentity,
    ICorrelationContext correlationContext)
    : TableInboxCRUD(scopedConnectionFactory)
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<InboxRecord> GetAsync(Guid fileId)
    {
        return await base.GetAsync(odinIdentity, fileId);
    }

    public new async Task<int> InsertAsync(InboxRecord item)
    {
        item.identityId = odinIdentity;

        if (item.timeStamp.milliseconds == 0)
            item.timeStamp = UnixTimeUtc.Now();

        item.correlationId = correlationContext.Id;
        return await base.InsertAsync(item);
    }

    public new async Task<int> UpsertAsync(InboxRecord item)
    {
        item.identityId = odinIdentity;

        if (item.timeStamp.milliseconds == 0)
            item.timeStamp = UnixTimeUtc.Now();

        item.correlationId = correlationContext.Id;
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

        if (count == int.MaxValue)
            count--; // avoid overflow when doing +1 on the param below

        cmd.CommandText =
            "UPDATE inbox SET popstamp=@popstamp WHERE rowid IN (SELECT rowid FROM inbox WHERE identityId=@identityId AND boxId=@boxId AND popstamp IS NULL ORDER BY rowId ASC LIMIT @count); " +
            "SELECT rowid,identityId,fileId,boxId,priority,timeStamp,value,popStamp,correlationId,created,modified FROM inbox WHERE identityId = @identityId AND popstamp=@popstamp ORDER BY rowId ASC";

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
        param4.Value = odinIdentity.IdentityIdAsByteArray();

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
        await using var tx = await cn.BeginStackedTransactionAsync(IsolationLevel.RepeatableRead);
        await using var cmd = cn.CreateCommand();

        // GROK:NOTE
        // Below sub-select needed for concurrency guarantee instead of using multiple SELECTs.
        // Also more performant than individual selects.
        cmd.CommandText =
            """
            SELECT 
               COUNT(*) AS total_count,
               SUM(CASE WHEN popstamp IS NOT NULL THEN 1 ELSE 0 END) AS popped_count,
               (SELECT popstamp 
                FROM inbox 
                WHERE identityId = @identityId AND popstamp IS NOT NULL 
                ORDER BY popstamp ASC LIMIT 1) AS oldest_popstamp
            FROM inbox 
            WHERE identityId = @identityId;
            """;

        var param1 = cmd.CreateParameter();
        param1.ParameterName = "@identityId";
        cmd.Parameters.Add(param1);
        param1.Value = odinIdentity.IdentityIdAsByteArray();

        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync())
        {
            throw new Exception("No results");
        }

        var totalCount = rdr[0] == DBNull.Value ? 0 : (int)(long)rdr[0];
        var poppedCount = rdr[1] == DBNull.Value ? 0 : (int)(long)rdr[1];

        var utc = UnixTimeUtc.ZeroTime;
        if (rdr[2] != DBNull.Value)
        {
            var bytes = (byte[])rdr[2];
            if (bytes.Length != 16)
            {
                throw new Exception("Invalid stamp");
            }
            var guid = new Guid(bytes);
            utc = SequentialGuid.ToUnixTimeUtc(guid);
        }

        return (totalCount, poppedCount, utc);
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

        // GROK:NOTE
        // Below sub-select needed for concurrency guarantee instead of using multiple SELECTs.
        // Also more performant than individual selects.
        cmd.CommandText =
            """
            SELECT 
               COUNT(*) AS total_count,
               SUM(CASE WHEN popstamp IS NOT NULL THEN 1 ELSE 0 END) AS popped_count,
               (SELECT popstamp 
                FROM inbox 
                WHERE identityId = @identityId AND boxId = @boxId AND popstamp IS NOT NULL 
                ORDER BY popstamp ASC LIMIT 1) AS oldest_popstamp
            FROM inbox 
            WHERE identityId = @identityId AND boxId = @boxId;
            """;

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        param1.ParameterName = "@identityId";
        param1.Value = odinIdentity.IdentityIdAsByteArray();
        param2.ParameterName = "@boxId";
        param2.Value = boxId.ToByteArray();
        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        await using var rdr = await cmd.ExecuteReaderAsync();

        if (!await rdr.ReadAsync())
        {
            throw new Exception("No results");
        }

        var totalCount = rdr[0] == DBNull.Value ? 0 : (int)(long)rdr[0];
        var poppedCount = rdr[1] == DBNull.Value ? 0 : (int)(long)rdr[1];

        var utc = UnixTimeUtc.ZeroTime;
        if (rdr[2] != DBNull.Value)
        {
            var bytes = (byte[])rdr[2];
            if (bytes.Length != 16)
            {
                throw new Exception("Invalid stamp");
            }
            var guid = new Guid(bytes);
            utc = SequentialGuid.ToUnixTimeUtc(guid);
        }

        return (totalCount, poppedCount, utc);
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
        param2.Value = odinIdentity.IdentityIdAsByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> PopCancelListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
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
        param3.Value = odinIdentity.IdentityIdAsByteArray();

        int n = 0;

        foreach (var id in listFileId)
        {
            param2.Value = id.ToByteArray();
            n += await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();

        return n;
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
        param2.Value = odinIdentity.IdentityIdAsByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// Commits (removes) the items previously popped with the supplied 'popstamp'
    /// </summary>
    /// <param name="popstamp"></param>
    public async Task<int> PopCommitListAsync(Guid popstamp, Guid driveId, List<Guid> listFileId)
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
        param3.Value = odinIdentity.IdentityIdAsByteArray();

        int n = 0;

        // I'd rather not do a TEXT statement, this seems safer but slower.
        foreach (var id in listFileId)
        {
            param2.Value = id.ToByteArray();
            n += await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();

        return n;
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
        param2.Value = odinIdentity.IdentityIdAsByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }

    // Change to internal
    public new async Task<(List<InboxRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
    {
        return await base.PagingByRowIdAsync(count, inCursor);
    }
}
