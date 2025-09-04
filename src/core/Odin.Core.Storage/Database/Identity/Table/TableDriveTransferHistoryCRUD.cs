using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveTransferHistoryRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid driveId { get; set; }
        public Guid fileId { get; set; }
        public OdinId remoteIdentityId { get; set; }
        public Int32 latestTransferStatus { get; set; }
        public Boolean isInOutbox { get; set; }
        public Guid? latestSuccessfullyDeliveredVersionTag { get; set; }
        public Boolean isReadByRecipient { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            latestSuccessfullyDeliveredVersionTag.AssertGuidNotEmpty("Guid parameter latestSuccessfullyDeliveredVersionTag cannot be set to Empty GUID.");
        }
    } // End of record DriveTransferHistoryRecord

    public abstract class TableDriveTransferHistoryCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "DriveTransferHistory";

        protected TableDriveTransferHistoryCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "DriveTransferHistory");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveTransferHistory IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveTransferHistory( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"fileId BYTEA NOT NULL, "
                   +"remoteIdentityId TEXT NOT NULL, "
                   +"latestTransferStatus BIGINT NOT NULL, "
                   +"isInOutbox BOOLEAN NOT NULL, "
                   +"latestSuccessfullyDeliveredVersionTag BYTEA , "
                   +"isReadByRecipient BOOLEAN NOT NULL "
                   +", UNIQUE(identityId,driveId,fileId,remoteIdentityId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0DriveTransferHistory ON DriveTransferHistory(identityId,driveId,fileId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveTransferHistory", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(DriveTransferHistoryRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@remoteIdentityId", DbType.String, item.remoteIdentityId.DomainName);
                insertCommand.AddParameter("@latestTransferStatus", DbType.Int32, item.latestTransferStatus);
                insertCommand.AddParameter("@isInOutbox", DbType.Boolean, item.isInOutbox);
                insertCommand.AddParameter("@latestSuccessfullyDeliveredVersionTag", DbType.Binary, item.latestSuccessfullyDeliveredVersionTag);
                insertCommand.AddParameter("@isReadByRecipient", DbType.Boolean, item.isReadByRecipient);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveTransferHistoryRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                insertCommand.AddParameter("@remoteIdentityId", DbType.String, item.remoteIdentityId.DomainName);
                insertCommand.AddParameter("@latestTransferStatus", DbType.Int32, item.latestTransferStatus);
                insertCommand.AddParameter("@isInOutbox", DbType.Boolean, item.isInOutbox);
                insertCommand.AddParameter("@latestSuccessfullyDeliveredVersionTag", DbType.Binary, item.latestSuccessfullyDeliveredVersionTag);
                insertCommand.AddParameter("@isReadByRecipient", DbType.Boolean, item.isReadByRecipient);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveTransferHistoryRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient)"+
                                            "ON CONFLICT (identityId,driveId,fileId,remoteIdentityId) DO UPDATE "+
                                            $"SET latestTransferStatus = @latestTransferStatus,isInOutbox = @isInOutbox,latestSuccessfullyDeliveredVersionTag = @latestSuccessfullyDeliveredVersionTag,isReadByRecipient = @isReadByRecipient "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                upsertCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                upsertCommand.AddParameter("@remoteIdentityId", DbType.String, item.remoteIdentityId.DomainName);
                upsertCommand.AddParameter("@latestTransferStatus", DbType.Int32, item.latestTransferStatus);
                upsertCommand.AddParameter("@isInOutbox", DbType.Boolean, item.isInOutbox);
                upsertCommand.AddParameter("@latestSuccessfullyDeliveredVersionTag", DbType.Binary, item.latestSuccessfullyDeliveredVersionTag);
                upsertCommand.AddParameter("@isReadByRecipient", DbType.Boolean, item.isReadByRecipient);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveTransferHistoryRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE DriveTransferHistory " +
                                            $"SET latestTransferStatus = @latestTransferStatus,isInOutbox = @isInOutbox,latestSuccessfullyDeliveredVersionTag = @latestSuccessfullyDeliveredVersionTag,isReadByRecipient = @isReadByRecipient "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                updateCommand.AddParameter("@fileId", DbType.Binary, item.fileId);
                updateCommand.AddParameter("@remoteIdentityId", DbType.String, item.remoteIdentityId.DomainName);
                updateCommand.AddParameter("@latestTransferStatus", DbType.Int32, item.latestTransferStatus);
                updateCommand.AddParameter("@isInOutbox", DbType.Boolean, item.isInOutbox);
                updateCommand.AddParameter("@latestSuccessfullyDeliveredVersionTag", DbType.Binary, item.latestSuccessfullyDeliveredVersionTag);
                updateCommand.AddParameter("@isReadByRecipient", DbType.Boolean, item.isReadByRecipient);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveTransferHistory;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public new static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("fileId");
            sl.Add("remoteIdentityId");
            sl.Add("latestTransferStatus");
            sl.Add("isInOutbox");
            sl.Add("latestSuccessfullyDeliveredVersionTag");
            sl.Add("isReadByRecipient");
            return sl;
        }

        protected virtual async Task<int> GetDriveCountAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "SELECT COUNT(*) FROM DriveTransferHistory WHERE driveId = $driveId;";
                getCountDriveCommand.AddParameter("$driveId", DbType.Binary, driveId);
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient
        protected DriveTransferHistoryRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveTransferHistoryRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveTransferHistoryRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.fileId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.remoteIdentityId = (rdr[4] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[4]);
            item.latestTransferStatus = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.isInOutbox = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[6]);
            item.latestSuccessfullyDeliveredVersionTag = (rdr[7] == DBNull.Value) ? null : new Guid((byte[])rdr[7]);
            item.isReadByRecipient = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[8]);
            return item;
       }

        protected virtual async Task<int> DeleteAllRowsAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete0Command.AddParameter("@fileId", DbType.Binary, fileId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid fileId,OdinId remoteIdentityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM DriveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId";

                delete1Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete1Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete1Command.AddParameter("@fileId", DbType.Binary, fileId);
                delete1Command.AddParameter("@remoteIdentityId", DbType.String, remoteIdentityId.DomainName);
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<DriveTransferHistoryRecord> PopAsync(Guid identityId,Guid driveId,Guid fileId,OdinId remoteIdentityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM DriveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId " + 
                                             "RETURNING rowId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
                deleteCommand.AddParameter("@fileId", DbType.Binary, fileId);
                deleteCommand.AddParameter("@remoteIdentityId", DbType.String, remoteIdentityId.DomainName);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,driveId,fileId,remoteIdentityId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected DriveTransferHistoryRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId,OdinId remoteIdentityId)
        {
            var result = new List<DriveTransferHistoryRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveTransferHistoryRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.remoteIdentityId = remoteIdentityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.latestTransferStatus = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.isInOutbox = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[2]);
            item.latestSuccessfullyDeliveredVersionTag = (rdr[3] == DBNull.Value) ? null : new Guid((byte[])rdr[3]);
            item.isReadByRecipient = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[4]);
            return item;
       }

        protected virtual async Task<DriveTransferHistoryRecord> GetAsync(Guid identityId,Guid driveId,Guid fileId,OdinId remoteIdentityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient FROM DriveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId LIMIT 1;"+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@driveId", DbType.Binary, driveId);
                get0Command.AddParameter("@fileId", DbType.Binary, fileId);
                get0Command.AddParameter("@remoteIdentityId", DbType.String, remoteIdentityId.DomainName);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,fileId,remoteIdentityId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveTransferHistoryRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid driveId,Guid fileId)
        {
            var result = new List<DriveTransferHistoryRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveTransferHistoryRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.remoteIdentityId = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.latestTransferStatus = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.isInOutbox = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[3]);
            item.latestSuccessfullyDeliveredVersionTag = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.isReadByRecipient = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[5]);
            return item;
       }

        protected virtual async Task<List<DriveTransferHistoryRecord>> GetAsync(Guid identityId,Guid driveId,Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient FROM DriveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;"+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@driveId", DbType.Binary, driveId);
                get1Command.AddParameter("@fileId", DbType.Binary, fileId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<DriveTransferHistoryRecord>();
                        }
                        var result = new List<DriveTransferHistoryRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,driveId,fileId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveTransferHistoryRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = 0;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging0Command = cn.CreateCommand();
            {
                getPaging0Command.CommandText = "SELECT rowId,identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient FROM DriveTransferHistory " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveTransferHistoryRecord>();
                        Int64? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
