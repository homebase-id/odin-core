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
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveTransferHistoryRecord
    {
        private Int64 _rowId;
        public Int64 rowId
        {
           get {
                   return _rowId;
               }
           set {
                  _rowId = value;
               }
        }
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _driveId;
        public Guid driveId
        {
           get {
                   return _driveId;
               }
           set {
                  _driveId = value;
               }
        }
        private Guid _fileId;
        public Guid fileId
        {
           get {
                   return _fileId;
               }
           set {
                  _fileId = value;
               }
        }
        private OdinId _remoteIdentityId;
        public OdinId remoteIdentityId
        {
           get {
                   return _remoteIdentityId;
               }
           set {
                  _remoteIdentityId = value;
               }
        }
        private Int32 _latestTransferStatus;
        public Int32 latestTransferStatus
        {
           get {
                   return _latestTransferStatus;
               }
           set {
                  _latestTransferStatus = value;
               }
        }
        private Boolean _isInOutbox;
        public Boolean isInOutbox
        {
           get {
                   return _isInOutbox;
               }
           set {
                  _isInOutbox = value;
               }
        }
        private Guid? _latestSuccessfullyDeliveredVersionTag;
        public Guid? latestSuccessfullyDeliveredVersionTag
        {
           get {
                   return _latestSuccessfullyDeliveredVersionTag;
               }
           set {
                  _latestSuccessfullyDeliveredVersionTag = value;
               }
        }
        private Boolean _isReadByRecipient;
        public Boolean isReadByRecipient
        {
           get {
                   return _isReadByRecipient;
               }
           set {
                  _isReadByRecipient = value;
               }
        }
        public void Validate()
        {
        }
    } // End of record DriveTransferHistoryRecord

    public abstract class TableDriveTransferHistoryCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveTransferHistoryCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task<int> EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS DriveTransferHistory;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS DriveTransferHistory("
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
            return await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> InsertAsync(DriveTransferHistoryRecord item)
        {
        identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                           $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@remoteIdentityId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@latestTransferStatus";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Boolean;
                insertParam6.ParameterName = "@isInOutbox";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Boolean;
                insertParam8.ParameterName = "@isReadByRecipient";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.remoteIdentityId.DomainName;
                insertParam5.Value = item.latestTransferStatus;
                insertParam6.Value = item.isInOutbox;
                insertParam7.Value = item.latestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.isReadByRecipient;
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
        identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@fileId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@remoteIdentityId";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int32;
                insertParam5.ParameterName = "@latestTransferStatus";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Boolean;
                insertParam6.ParameterName = "@isInOutbox";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.Binary;
                insertParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.Boolean;
                insertParam8.ParameterName = "@isReadByRecipient";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.fileId.ToByteArray();
                insertParam4.Value = item.remoteIdentityId.DomainName;
                insertParam5.Value = item.latestTransferStatus;
                insertParam6.Value = item.isInOutbox;
                insertParam7.Value = item.latestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                insertParam8.Value = item.isReadByRecipient;
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
        identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO DriveTransferHistory (identityId,driveId,fileId,remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient) " +
                                            $"VALUES (@identityId,@driveId,@fileId,@remoteIdentityId,@latestTransferStatus,@isInOutbox,@latestSuccessfullyDeliveredVersionTag,@isReadByRecipient)"+
                                            "ON CONFLICT (identityId,driveId,fileId,remoteIdentityId) DO UPDATE "+
                                            $"SET latestTransferStatus = @latestTransferStatus,isInOutbox = @isInOutbox,latestSuccessfullyDeliveredVersionTag = @latestSuccessfullyDeliveredVersionTag,isReadByRecipient = @isReadByRecipient "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@fileId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@remoteIdentityId";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int32;
                upsertParam5.ParameterName = "@latestTransferStatus";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Boolean;
                upsertParam6.ParameterName = "@isInOutbox";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.Binary;
                upsertParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.Boolean;
                upsertParam8.ParameterName = "@isReadByRecipient";
                upsertCommand.Parameters.Add(upsertParam8);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.fileId.ToByteArray();
                upsertParam4.Value = item.remoteIdentityId.DomainName;
                upsertParam5.Value = item.latestTransferStatus;
                upsertParam6.Value = item.isInOutbox;
                upsertParam7.Value = item.latestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                upsertParam8.Value = item.isReadByRecipient;
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
        identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
        driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
        fileId.AssertGuidNotEmpty("Guid parameter fileId cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE DriveTransferHistory " +
                                            $"SET latestTransferStatus = @latestTransferStatus,isInOutbox = @isInOutbox,latestSuccessfullyDeliveredVersionTag = @latestSuccessfullyDeliveredVersionTag,isReadByRecipient = @isReadByRecipient "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@fileId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@remoteIdentityId";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int32;
                updateParam5.ParameterName = "@latestTransferStatus";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Boolean;
                updateParam6.ParameterName = "@isInOutbox";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.Binary;
                updateParam7.ParameterName = "@latestSuccessfullyDeliveredVersionTag";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.Boolean;
                updateParam8.ParameterName = "@isReadByRecipient";
                updateCommand.Parameters.Add(updateParam8);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.fileId.ToByteArray();
                updateParam4.Value = item.remoteIdentityId.DomainName;
                updateParam5.Value = item.latestTransferStatus;
                updateParam6.Value = item.isInOutbox;
                updateParam7.Value = item.latestSuccessfullyDeliveredVersionTag?.ToByteArray() ?? (object)DBNull.Value;
                updateParam8.Value = item.isReadByRecipient;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
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

        public static List<string> GetColumnNames()
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
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.DbType = DbType.Binary;
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
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
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.DbType = DbType.Binary;
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = fileId.ToByteArray();
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
                var delete1Param1 = delete1Command.CreateParameter();
                delete1Param1.DbType = DbType.Binary;
                delete1Param1.ParameterName = "@identityId";
                delete1Command.Parameters.Add(delete1Param1);
                var delete1Param2 = delete1Command.CreateParameter();
                delete1Param2.DbType = DbType.Binary;
                delete1Param2.ParameterName = "@driveId";
                delete1Command.Parameters.Add(delete1Param2);
                var delete1Param3 = delete1Command.CreateParameter();
                delete1Param3.DbType = DbType.Binary;
                delete1Param3.ParameterName = "@fileId";
                delete1Command.Parameters.Add(delete1Param3);
                var delete1Param4 = delete1Command.CreateParameter();
                delete1Param4.DbType = DbType.String;
                delete1Param4.ParameterName = "@remoteIdentityId";
                delete1Command.Parameters.Add(delete1Param4);

                delete1Param1.Value = identityId.ToByteArray();
                delete1Param2.Value = driveId.ToByteArray();
                delete1Param3.Value = fileId.ToByteArray();
                delete1Param4.Value = remoteIdentityId.DomainName;
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
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@driveId";
                deleteCommand.Parameters.Add(deleteParam2);
                var deleteParam3 = deleteCommand.CreateParameter();
                deleteParam3.DbType = DbType.Binary;
                deleteParam3.ParameterName = "@fileId";
                deleteCommand.Parameters.Add(deleteParam3);
                var deleteParam4 = deleteCommand.CreateParameter();
                deleteParam4.DbType = DbType.String;
                deleteParam4.ParameterName = "@remoteIdentityId";
                deleteCommand.Parameters.Add(deleteParam4);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = driveId.ToByteArray();
                deleteParam3.Value = fileId.ToByteArray();
                deleteParam4.Value = remoteIdentityId.DomainName;
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
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.DbType = DbType.Binary;
                get0Param3.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
                get0Param4.DbType = DbType.String;
                get0Param4.ParameterName = "@remoteIdentityId";
                get0Command.Parameters.Add(get0Param4);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = fileId.ToByteArray();
                get0Param4.Value = remoteIdentityId.DomainName;
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
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.DbType = DbType.Binary;
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.DbType = DbType.Binary;
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
                get1Param3.DbType = DbType.Binary;
                get1Param3.ParameterName = "@fileId";
                get1Command.Parameters.Add(get1Param3);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = driveId.ToByteArray();
                get1Param3.Value = fileId.ToByteArray();
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
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.DbType = DbType.Int64;
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.DbType = DbType.Int64;
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

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
