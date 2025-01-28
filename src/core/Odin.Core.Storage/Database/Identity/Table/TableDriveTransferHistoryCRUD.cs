using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class DriveTransferHistoryRecord
    {
        private Guid _identityId;

        public Guid identityId
        {
            get { return _identityId; }
            set { _identityId = value; }
        }

        private Guid _driveId;

        public Guid driveId
        {
            get { return _driveId; }
            set { _driveId = value; }
        }

        private Guid _fileId;

        public Guid fileId
        {
            get { return _fileId; }
            set { _fileId = value; }
        }

        private OdinId _remoteIdentityId;

        public OdinId remoteIdentityId
        {
            get { return _remoteIdentityId; }
            set { _remoteIdentityId = value; }
        }

        private Int32 _latestTransferStatus;

        public Int32 latestTransferStatus
        {
            get { return _latestTransferStatus; }
            set { _latestTransferStatus = value; }
        }

        private Int32 _isInOutbox;

        public Int32 isInOutbox
        {
            get { return _isInOutbox; }
            set { _isInOutbox = value; }
        }

        private Guid? _latestSuccessfullyDeliveredVersionTag;

        public Guid? latestSuccessfullyDeliveredVersionTag
        {
            get { return _latestSuccessfullyDeliveredVersionTag; }
            set { _latestSuccessfullyDeliveredVersionTag = value; }
        }

        private Int32 _isReadByRecipient;

        public Int32 isReadByRecipient
        {
            get { return _isReadByRecipient; }
            set { _isReadByRecipient = value; }
        }
    } // End of class DriveTransferHistoryRecord

    public abstract class TableDriveTransferHistoryCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveTransferHistoryCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS driveTransferHistory;";
                await cmd.ExecuteNonQueryAsync();
            }

            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
            {
                rowid = ", rowid BIGSERIAL NOT NULL UNIQUE ";
            }

            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS driveTransferHistory("
                + "identityId BYTEA NOT NULL, "
                + "driveId BYTEA NOT NULL, "
                + "fileId BYTEA NOT NULL, "
                + "remoteIdentityId TEXT NOT NULL, "
                + "latestTransferStatus BIGINT , "
                + "isInOutbox BOOLEAN , "
                + "latestSuccessfullyDeliveredVersionTag BYTEA , "
                + "isReadByRecipient BOOLEAN  "
                + rowid
                + ", PRIMARY KEY (identityId,driveId,fileId,remoteIdentityId)"
                + ");"
                + "CREATE INDEX IF NOT EXISTS Idx0TableDriveTransferHistoryCRUD ON driveTransferHistory(identityId,driveId,fileId);"
                ;
            await cmd.ExecuteNonQueryAsync();
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM driveTransferHistory;";
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

        protected virtual async Task<int> DeleteAsync(Guid identityId, Guid driveId, Guid fileId, OdinId remoteIdentityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM driveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.ParameterName = "@fileId";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.ParameterName = "@remoteIdentityId";
                delete0Command.Parameters.Add(delete0Param4);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = fileId.ToByteArray();
                delete0Param4.Value = remoteIdentityId.DomainName;
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAllRowsAsync(Guid identityId, Guid driveId, Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM driveTransferHistory " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId";
                var delete1Param1 = delete1Command.CreateParameter();
                delete1Param1.ParameterName = "@identityId";
                delete1Command.Parameters.Add(delete1Param1);
                var delete1Param2 = delete1Command.CreateParameter();
                delete1Param2.ParameterName = "@driveId";
                delete1Command.Parameters.Add(delete1Param2);
                var delete1Param3 = delete1Command.CreateParameter();
                delete1Param3.ParameterName = "@fileId";
                delete1Command.Parameters.Add(delete1Param3);

                delete1Param1.Value = identityId.ToByteArray();
                delete1Param2.Value = driveId.ToByteArray();
                delete1Param3.Value = fileId.ToByteArray();
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected DriveTransferHistoryRecord ReadRecordFromReader0(DbDataReader rdr, Guid identityId, Guid driveId, Guid fileId,
            OdinId remoteIdentityId)
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
            item.latestTransferStatus = rdr.IsDBNull(0) ? 0 : (int)(long)rdr[0];
            item.isInOutbox = rdr.IsDBNull(1) ? 0 : (int)(long)rdr[1];
            item.latestSuccessfullyDeliveredVersionTag = rdr.IsDBNull(2) ? null : new Guid((byte[])rdr[2]);
            item.isReadByRecipient = rdr.IsDBNull(3) ? 0 : (int)(long)rdr[3];
            return item;
        }

        protected virtual async Task<DriveTransferHistoryRecord> GetAsync(Guid identityId, Guid driveId, Guid fileId,
            OdinId remoteIdentityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText =
                    "SELECT latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient FROM driveTransferHistory " +
                    "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId AND remoteIdentityId = @remoteIdentityId LIMIT 1;";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.ParameterName = "@fileId";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
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

                        var r = ReadRecordFromReader0(rdr, identityId, driveId, fileId, remoteIdentityId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveTransferHistoryRecord ReadRecordFromReader1(DbDataReader rdr, Guid identityId,Guid driveId,Guid fileId)
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
            item.remoteIdentityId = rdr.IsDBNull(0) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[0]);
            item.latestTransferStatus = rdr.IsDBNull(1) ? 0 : (int)(long)rdr[1];
            item.isInOutbox = rdr.IsDBNull(2) ? 0 : (int)(long)rdr[2];
            item.latestSuccessfullyDeliveredVersionTag = rdr.IsDBNull(3) ? null : new Guid((byte[])rdr[3]);
            item.isReadByRecipient = rdr.IsDBNull(4) ? 0 : (int)(long)rdr[4];
            return item;
        }

        protected virtual async Task<List<DriveTransferHistoryRecord>> GetAsync(Guid identityId, Guid driveId, Guid fileId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText =
                    "SELECT remoteIdentityId,latestTransferStatus,isInOutbox,latestSuccessfullyDeliveredVersionTag,isReadByRecipient FROM driveTransferHistory " +
                    "WHERE identityId = @identityId AND driveId = @driveId AND fileId = @fileId;";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@driveId";
                get1Command.Parameters.Add(get1Param2);
                var get1Param3 = get1Command.CreateParameter();
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
                            result.Add(ReadRecordFromReader1(rdr, identityId, driveId, fileId));
                            if (!await rdr.ReadAsync())
                                break;
                        }

                        return result;
                    } // using
                } //
            } // using
        }
    }
}