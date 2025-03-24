using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Time;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableOutbox(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory,
    IdentityKey identityKey,
    ICorrelationContext correlationContext)
    : TableOutboxCRUD(cache, scopedConnectionFactory), ITableMigrator
{
    private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory = scopedConnectionFactory;

    public async Task<List<OutboxRecord>> GetAsync(Guid driveId, Guid fileId)
    {
        return await base.GetAsync(identityKey, driveId, fileId);
    }

    public async Task<OutboxRecord> GetAsync(Guid driveId, Guid fileId, string recipient)
    {
        return await base.GetAsync(identityKey, driveId, fileId, recipient);
    }

    public new async Task<int> InsertAsync(OutboxRecord item)
    {
        item.identityId = identityKey;
        item.checkOutCount = 0;
        if (item.nextRunTime.milliseconds == 0)
            item.nextRunTime = UnixTimeUtc.Now();

        if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
            throw new OdinSystemException("You're not allowed to make an item dependent on itself as it would deadlock the item.");

        item.correlationId = correlationContext.Id;
        return await base.InsertAsync(item);
    }


    public new async Task<int> UpsertAsync(OutboxRecord item)
    {
        if (ByteArrayUtil.muidcmp(item.fileId, item.dependencyFileId) == 0)
            throw new Exception("You're not allowed to make an item dependent on itself as it would deadlock the item.");

        item.identityId = identityKey;
        if (item.nextRunTime.milliseconds == 0)
            item.nextRunTime = UnixTimeUtc.Now();

        item.correlationId = correlationContext.Id;
        return await base.UpsertAsync(item);
    }


    /// <summary>
    /// Will check out the next item to process. Will not check out items scheduled for the future
    /// or items that have unresolved dependencies.
    /// </summary>
    /// <returns></returns>
    public async Task<OutboxRecord> CheckOutItemAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = """
                                    UPDATE outbox
                                    SET checkOutStamp = @checkOutStamp
                                    WHERE identityId=@identityId AND (checkOutStamp is NULL) AND
                                      rowId = (
                                          SELECT rowId
                                          FROM outbox
                                          WHERE identityId=@identityId AND checkOutStamp IS NULL
                                          AND nextRunTime <= @now
                                          AND (
                                            (dependencyFileId IS NULL)
                                            OR (NOT EXISTS (
                                                  SELECT 1
                                                  FROM outbox AS ib
                                                  WHERE ib.identityId = outbox.identityId
                                                  AND ib.fileId = outbox.dependencyFileId
                                                  AND ib.recipient = outbox.recipient
                                            ))
                                          )
                                          ORDER BY priority ASC, nextRunTime ASC
                                          LIMIT 1
                                    );
                                    SELECT rowid,identityId,driveId,fileId,recipient,type,priority,dependencyFileId,checkOutCount,nextRunTime,value,checkOutStamp,correlationId,created,modified
                                    FROM outbox
                                    WHERE identityId=@identityId AND checkOutStamp=@checkOutStamp;
                                    """;
        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@checkOutStamp";
        param2.ParameterName = "@identityId";
        param3.ParameterName = "@now";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = SequentialGuid.CreateGuid().ToByteArray();
        param2.Value = identityKey.ToByteArray();
        param3.Value = UnixTimeUtc.Now().milliseconds;

        var result = new List<OutboxRecord>();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            if (await rdr.ReadAsync())
            {
                return ReadRecordFromReaderAll(rdr);
            }
            else
                return null;
        }
    }


    /// <summary>
    /// Returns when the next outbox item ís scheduled to be sent. If the returned time is larger than UnixTimeUtc.Now()
    /// it is in the future and you should schedule a job to activate at that time. If null there is nothing in the outbox
    /// to be sent.
    /// Remember to cancel any pending outbox jobs, also if you're setting a schedule.
    /// </summary>
    /// <returns>UnixTimeUtc of when the next item should be sent, null if none.</returns>
    /// <exception cref="Exception"></exception>
    public async Task<UnixTimeUtc?> NextScheduledItemAsync(Guid? driveId = null)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        string s = "";
        if (driveId != null)
            s = "AND driveId=@driveId";


        cmd.CommandText =$"""
                             SELECT nextRunTime 
                             FROM outbox 
                             WHERE identityId=@identityId {s} AND checkOutStamp IS NULL AND
                                      rowId = (
                                          SELECT rowId
                                          FROM outbox
                                          WHERE identityId=@identityId AND checkOutStamp IS NULL
                                              AND (
                                                (dependencyFileId IS NULL)
                                                OR (NOT EXISTS (
                                                      SELECT 1
                                                      FROM outbox AS ib
                                                      WHERE ib.identityId = outbox.identityId
                                                      AND ib.fileId = outbox.dependencyFileId
                                                      AND ib.recipient = outbox.recipient
                                                ))
                                              )
                                          ORDER BY priority ASC, nextRunTime ASC
                                          LIMIT 1
                                    );
                           """;



        var param1 = cmd.CreateParameter();
        param1.ParameterName = "@identityId";
        cmd.Parameters.Add(param1);

        var param2 = cmd.CreateParameter();

        if (driveId != null)
        {            
            param2.ParameterName = "@driveId";
            cmd.Parameters.Add(param2);
        }

        param1.Value = identityKey.ToByteArray();
        if (driveId != null)
            param2.Value = driveId?.ToByteArray();

        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            // Read the total count
            if (await rdr.ReadAsync() == false)
                return null;
            if ((rdr[0] == DBNull.Value))
                throw new Exception("Not possible");

            long nextRunTime = (Int64) rdr[0];
            return new UnixTimeUtc(nextRunTime);
        }
    }


    /// <summary>
    /// Cancels the pop of items with the 'checkOutStamp' from a previous pop operation
    /// </summary>
    /// <param name="checkOutStamp"></param>
    public async Task<int> CheckInAsCancelledAsync(Guid checkOutStamp, UnixTimeUtc nextRunTime)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "UPDATE outbox SET checkOutStamp=NULL, checkOutCount=checkOutCount+1, nextRunTime=@nextRunTime WHERE identityId=@identityId AND checkOutStamp=@checkOutStamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();
        var param3 = cmd.CreateParameter();

        param1.ParameterName = "@checkOutStamp";
        param2.ParameterName = "@nextRunTime";
        param3.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);
        cmd.Parameters.Add(param3);

        param1.Value = checkOutStamp.ToByteArray();
        param2.Value = nextRunTime.milliseconds;
        param3.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }



    /// <summary>
    /// Commits (removes) the items previously popped with the supplied 'checkOutStamp'
    /// </summary>
    /// <param name="checkOutStamp"></param>
    public async Task<int> CompleteAndRemoveAsync(Guid checkOutStamp)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "DELETE FROM outbox WHERE identityId=@identityId AND checkOutStamp=@checkOutStamp";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@checkOutStamp";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = checkOutStamp.ToByteArray();
        param2.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Recover popped items older than the supplied UnixTime in seconds.
    /// This is how to recover popped items that were never processed for example on a server crash.
    /// Call with e.g. a time of more than 5 minutes ago.
    /// </summary>
    public async Task<int> RecoverCheckedOutDeadItemsAsync(UnixTimeUtc pastThreshold)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText = "UPDATE outbox SET checkOutStamp=NULL,checkOutCount=checkOutCount+1 WHERE identityId=@identityId AND checkOutStamp < @checkOutStamp";

        // Should we also reset nextRunTime =@nextRunTime to "now()" or 0?
        //
        // Consider removing any items with checkOutCount == 0 older than X
        // since they are probably circular dependencies

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@checkOutStamp";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = SequentialGuid.CreateGuid(pastThreshold).ToByteArray(); // UnixTimeMiliseconds
        param2.Value = identityKey.ToByteArray();

        return await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// Status on the box
    /// </summary>
    /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(int totalItems, int checkedOutItems, UnixTimeUtc nextRunTime)> OutboxStatusAsync()
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "SELECT count(*) FROM outbox WHERE identityId=@identityId;" +
            "SELECT count(*) FROM outbox WHERE identityId=@identityId AND checkOutStamp IS NOT NULL;";
            
            // Wrong "SELECT nextRunTime FROM outbox WHERE identityId=@identityId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

        var param1 = cmd.CreateParameter();
        param1.ParameterName = "@identityId";
        cmd.Parameters.Add(param1);
        param1.Value = identityKey.ToByteArray();

        int totalCount = 0;
        int poppedCount = 0;
        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            // Read the total count
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            if (!(rdr[0] == DBNull.Value))
                totalCount = (int)(Int64) rdr[0];

            // Read the popped count
            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            if (!(rdr[0] == DBNull.Value))
                poppedCount = (int)(Int64) rdr[0];
        }
        var utc = await NextScheduledItemAsync() ?? UnixTimeUtc.ZeroTime;
        return (totalCount, poppedCount, utc);
    }

    /// <summary>
    /// Status on the box
    /// </summary>
    /// <returns>Number of total items in box, number of popped items, the next item schduled time (ZeroTime if none)</returns>
    /// <exception cref="Exception"></exception>
    public async Task<(int, int, UnixTimeUtc)> OutboxStatusDriveAsync(Guid driveId)
    {
        await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
        await using var cmd = cn.CreateCommand();

        cmd.CommandText =
            "SELECT count(*) FROM outbox WHERE identityId=@identityId AND driveId=@driveId;" +
            "SELECT count(*) FROM outbox WHERE identityId=@identityId AND driveId=@driveId AND checkOutStamp IS NOT NULL;";
            // "SELECT nextRunTime FROM outbox WHERE identityId=@identityId AND driveId=@driveId AND checkOutStamp IS NULL ORDER BY nextRunTime ASC LIMIT 1;";

        var param1 = cmd.CreateParameter();
        var param2 = cmd.CreateParameter();

        param1.ParameterName = "@driveId";
        param2.ParameterName = "@identityId";

        cmd.Parameters.Add(param1);
        cmd.Parameters.Add(param2);

        param1.Value = driveId.ToByteArray();
        param2.Value = identityKey.ToByteArray();

        int totalCount = 0;
        int poppedCount = 0;
        using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.Default))
        {
            // Read the total count
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            if (!(rdr[0] == DBNull.Value))
                totalCount = (int)(Int64) rdr[0];

            // Read the popped count
            if (await rdr.NextResultAsync() == false)
                throw new Exception("Not possible");
            if (await rdr.ReadAsync() == false)
                throw new Exception("Not possible");

            if (!(rdr[0] == DBNull.Value))
                poppedCount = (int)(Int64) rdr[0];
        }
        var utc = await NextScheduledItemAsync(driveId) ?? UnixTimeUtc.ZeroTime;
        return (totalCount, poppedCount, utc);
    }
}