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

        private bool _isInOutbox;

        public bool isInOutbox
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

        private bool _isReadByRecipient;

        public bool isReadByRecipient
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
        
        
    }
}