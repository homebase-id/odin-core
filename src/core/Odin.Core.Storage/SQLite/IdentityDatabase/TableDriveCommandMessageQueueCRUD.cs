using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveCommandMessageQueueRecord
    {
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
        private UnixTimeUtc _timeStamp;
        public UnixTimeUtc timeStamp
        {
           get {
                   return _timeStamp;
               }
           set {
                  _timeStamp = value;
               }
        }
    } // End of class DriveCommandMessageQueueRecord

    public class TableDriveCommandMessageQueueCRUD : TableBase
    {
        private bool _disposed = false;

        public TableDriveCommandMessageQueueCRUD(IdentityDatabase db, CacheHelper cache) : base(db, "driveCommandMessageQueue")
        {
        }

        ~TableDriveCommandMessageQueueCRUD()
        {
            if (_disposed == false) throw new Exception("TableDriveCommandMessageQueueCRUD Not disposed properly");
        }

        public override void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public sealed override void EnsureTableExists(DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveCommandMessageQueue;";
                       conn.ExecuteNonQuery(cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveCommandMessageQueue("
                     +"identityId BLOB NOT NULL, "
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"timeStamp INT NOT NULL "
                     +", PRIMARY KEY (identityId,driveId,fileId)"
                     +");"
                     ;
                    conn.ExecuteNonQuery(cmd);
            }
        }

        protected virtual int Insert(DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
            using (var _insertCommand = _database.CreateCommand())
            {
                _insertCommand.CommandText = "INSERT INTO driveCommandMessageQueue (identityId,driveId,fileId,timeStamp) " +
                                             "VALUES ($identityId,$driveId,$fileId,$timeStamp)";
                var _insertParam1 = _insertCommand.CreateParameter();
                _insertParam1.ParameterName = "$identityId";
                _insertCommand.Parameters.Add(_insertParam1);
                var _insertParam2 = _insertCommand.CreateParameter();
                _insertParam2.ParameterName = "$driveId";
                _insertCommand.Parameters.Add(_insertParam2);
                var _insertParam3 = _insertCommand.CreateParameter();
                _insertParam3.ParameterName = "$fileId";
                _insertCommand.Parameters.Add(_insertParam3);
                var _insertParam4 = _insertCommand.CreateParameter();
                _insertParam4.ParameterName = "$timeStamp";
                _insertCommand.Parameters.Add(_insertParam4);
                _insertParam1.Value = item.identityId.ToByteArray();
                _insertParam2.Value = item.driveId.ToByteArray();
                _insertParam3.Value = item.fileId.ToByteArray();
                _insertParam4.Value = item.timeStamp.milliseconds;
                var count = conn.ExecuteNonQuery(_insertCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        protected virtual int Upsert(DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO driveCommandMessageQueue (identityId,driveId,fileId,timeStamp) " +
                                             "VALUES ($identityId,$driveId,$fileId,$timeStamp)"+
                                             "ON CONFLICT (identityId,driveId,fileId) DO UPDATE "+
                                             "SET timeStamp = $timeStamp "+
                                             ";";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "$identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "$driveId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "$fileId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "$timeStamp";
                _upsertCommand.Parameters.Add(_upsertParam4);
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.fileId.ToByteArray();
                _upsertParam4.Value = item.timeStamp.milliseconds;
                var count = conn.ExecuteNonQuery(_upsertCommand);
                return count;
            } // Using
        }
        protected virtual int Update(DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE driveCommandMessageQueue " +
                                             "SET timeStamp = $timeStamp "+
                                             "WHERE (identityId = $identityId AND driveId = $driveId AND fileId = $fileId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "$identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "$driveId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "$fileId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "$timeStamp";
                _updateCommand.Parameters.Add(_updateParam4);
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.fileId.ToByteArray();
                _updateParam4.Value = item.timeStamp.milliseconds;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                }
                return count;
            } // Using
        }

        protected virtual int GetCount(DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveCommandMessageQueue; PRAGMA read_uncommitted = 0;";
                    var count = conn.ExecuteScalar(_getCountCommand);
                    if (count == null || count == DBNull.Value || !(count is int || count is long))
                        return -1;
                    else
                        return Convert.ToInt32(count);
                }
        }

        public override List<string> GetColumnNames()
        {
                var sl = new List<string>();
                sl.Add("identityId");
                sl.Add("driveId");
                sl.Add("fileId");
                sl.Add("timeStamp");
            return sl;
        }

        protected virtual int GetDriveCount(DatabaseConnection conn, Guid driveId)
        {
                using (var _getCountDriveCommand = _database.CreateCommand())
                {
                    _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveCommandMessageQueue WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                    var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                    _getCountDriveParam1.ParameterName = "$driveId";
                    _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                    _getCountDriveParam1.Value = driveId.ToByteArray();
                    var count = conn.ExecuteScalar(_getCountDriveCommand);
                    if (count == null || count == DBNull.Value || !(count is int || count is long))
                        return -1;
                    else
                        return Convert.ToInt32(count);
                } // using
        }

        // SELECT identityId,driveId,fileId,timeStamp
        protected DriveCommandMessageQueueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
        {
            var result = new List<DriveCommandMessageQueueRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveCommandMessageQueueRecord();

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(0, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in identityId...");
                item.identityId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(2, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(3))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(3));
            }
            return item;
       }

        protected int Delete(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _delete0Command = _database.CreateCommand())
            {
                _delete0Command.CommandText = "DELETE FROM driveCommandMessageQueue " +
                                             "WHERE identityId = $identityId AND driveId = $driveId AND fileId = $fileId";
                var _delete0Param1 = _delete0Command.CreateParameter();
                _delete0Param1.ParameterName = "$identityId";
                _delete0Command.Parameters.Add(_delete0Param1);
                var _delete0Param2 = _delete0Command.CreateParameter();
                _delete0Param2.ParameterName = "$driveId";
                _delete0Command.Parameters.Add(_delete0Param2);
                var _delete0Param3 = _delete0Command.CreateParameter();
                _delete0Param3.ParameterName = "$fileId";
                _delete0Command.Parameters.Add(_delete0Param3);

                _delete0Param1.Value = identityId.ToByteArray();
                _delete0Param2.Value = driveId.ToByteArray();
                _delete0Param3.Value = fileId.ToByteArray();
                var count = conn.ExecuteNonQuery(_delete0Command);
                return count;
            } // Using
        }

        protected DriveCommandMessageQueueRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid identityId,Guid driveId,Guid fileId)
        {
            var result = new List<DriveCommandMessageQueueRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveCommandMessageQueueRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.fileId = fileId;

            if (rdr.IsDBNull(0))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(0));
            }
            return item;
       }

        protected DriveCommandMessageQueueRecord Get(DatabaseConnection conn, Guid identityId,Guid driveId,Guid fileId)
        {
            using (var _get0Command = _database.CreateCommand())
            {
                _get0Command.CommandText = "SELECT timeStamp FROM driveCommandMessageQueue " +
                                             "WHERE identityId = $identityId AND driveId = $driveId AND fileId = $fileId LIMIT 1;";
                var _get0Param1 = _get0Command.CreateParameter();
                _get0Param1.ParameterName = "$identityId";
                _get0Command.Parameters.Add(_get0Param1);
                var _get0Param2 = _get0Command.CreateParameter();
                _get0Param2.ParameterName = "$driveId";
                _get0Command.Parameters.Add(_get0Param2);
                var _get0Param3 = _get0Command.CreateParameter();
                _get0Param3.ParameterName = "$fileId";
                _get0Command.Parameters.Add(_get0Param3);

                _get0Param1.Value = identityId.ToByteArray();
                _get0Param2.Value = driveId.ToByteArray();
                _get0Param3.Value = fileId.ToByteArray();
                lock (conn._lock)
                {
                using (SqliteDataReader rdr = conn.ExecuteReader(_get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, identityId,driveId,fileId);
                    return r;
                } // using
                } // lock
            } // using
        }

    }
}
