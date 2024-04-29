using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Odin.Core.Identity;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class DriveCommandMessageQueueRecord
    {
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

        public TableDriveCommandMessageQueueCRUD(IdentityDatabase db, CacheHelper cache) : base(db)
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

        public sealed override void EnsureTableExists(DatabaseBase.DatabaseConnection conn, bool dropExisting = false)
        {
                using (var cmd = _database.CreateCommand())
                {
                    if (dropExisting)
                    {
                       cmd.CommandText = "DROP TABLE IF EXISTS driveCommandMessageQueue;";
                        _database.ExecuteNonQuery(conn, cmd);
                    }
                    cmd.CommandText =
                    "CREATE TABLE IF NOT EXISTS driveCommandMessageQueue("
                     +"driveId BLOB NOT NULL, "
                     +"fileId BLOB NOT NULL, "
                     +"timeStamp INT NOT NULL "
                     +", PRIMARY KEY (driveId,fileId)"
                     +");"
                     ;
                    _database.ExecuteNonQuery(conn, cmd);
            }
        }

        public virtual int Insert(DatabaseBase.DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
                using (var _insertCommand = _database.CreateCommand())
                {
                    _insertCommand.CommandText = "INSERT INTO driveCommandMessageQueue (driveId,fileId,timeStamp) " +
                                                 "VALUES ($driveId,$fileId,$timeStamp)";
                    var _insertParam1 = _insertCommand.CreateParameter();
                    _insertParam1.ParameterName = "$driveId";
                    _insertCommand.Parameters.Add(_insertParam1);
                    var _insertParam2 = _insertCommand.CreateParameter();
                    _insertParam2.ParameterName = "$fileId";
                    _insertCommand.Parameters.Add(_insertParam2);
                    var _insertParam3 = _insertCommand.CreateParameter();
                    _insertParam3.ParameterName = "$timeStamp";
                    _insertCommand.Parameters.Add(_insertParam3);
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.timeStamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                if (count > 0)
                 {
                 }
                return count;
                } // Using
        }

        public virtual int Upsert(DatabaseBase.DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
                using (var _upsertCommand = _database.CreateCommand())
                {
                    _upsertCommand.CommandText = "INSERT INTO driveCommandMessageQueue (driveId,fileId,timeStamp) " +
                                                 "VALUES ($driveId,$fileId,$timeStamp)"+
                                                 "ON CONFLICT (driveId,fileId) DO UPDATE "+
                                                 "SET timeStamp = $timeStamp "+
                                                 ";";
                    var _upsertParam1 = _upsertCommand.CreateParameter();
                    _upsertParam1.ParameterName = "$driveId";
                    _upsertCommand.Parameters.Add(_upsertParam1);
                    var _upsertParam2 = _upsertCommand.CreateParameter();
                    _upsertParam2.ParameterName = "$fileId";
                    _upsertCommand.Parameters.Add(_upsertParam2);
                    var _upsertParam3 = _upsertCommand.CreateParameter();
                    _upsertParam3.ParameterName = "$timeStamp";
                    _upsertCommand.Parameters.Add(_upsertParam3);
                _upsertParam1.Value = item.driveId.ToByteArray();
                _upsertParam2.Value = item.fileId.ToByteArray();
                _upsertParam3.Value = item.timeStamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _upsertCommand);
                return count;
                } // Using
        }
        public virtual int Update(DatabaseBase.DatabaseConnection conn, DriveCommandMessageQueueRecord item)
        {
                using (var _updateCommand = _database.CreateCommand())
                {
                    _updateCommand.CommandText = "UPDATE driveCommandMessageQueue " +
                                                 "SET timeStamp = $timeStamp "+
                                                 "WHERE (driveId = $driveId,fileId = $fileId)";
                    var _updateParam1 = _updateCommand.CreateParameter();
                    _updateParam1.ParameterName = "$driveId";
                    _updateCommand.Parameters.Add(_updateParam1);
                    var _updateParam2 = _updateCommand.CreateParameter();
                    _updateParam2.ParameterName = "$fileId";
                    _updateCommand.Parameters.Add(_updateParam2);
                    var _updateParam3 = _updateCommand.CreateParameter();
                    _updateParam3.ParameterName = "$timeStamp";
                    _updateCommand.Parameters.Add(_updateParam3);
                _updateParam1.Value = item.driveId.ToByteArray();
                _updateParam2.Value = item.fileId.ToByteArray();
                _updateParam3.Value = item.timeStamp.milliseconds;
                var count = _database.ExecuteNonQuery(conn, _updateCommand);
                if (count > 0)
                {
                }
                return count;
                } // Using
        }

        public virtual int GetCount(DatabaseBase.DatabaseConnection conn)
        {
                using (var _getCountCommand = _database.CreateCommand())
                {
                    _getCountCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveCommandMessageQueue; PRAGMA read_uncommitted = 0;";
                    var count = _database.ExecuteNonQuery(conn, _getCountCommand);
                    return count;
                }
        }

        public virtual int GetDriveCount(DatabaseBase.DatabaseConnection conn, Guid driveId)
        {
                using (var _getCountDriveCommand = _database.CreateCommand())
                {
                    _getCountDriveCommand.CommandText = "PRAGMA read_uncommitted = 1; SELECT COUNT(*) FROM driveCommandMessageQueue WHERE driveId = $driveId;PRAGMA read_uncommitted = 0;";
                    var _getCountDriveParam1 = _getCountDriveCommand.CreateParameter();
                    _getCountDriveParam1.ParameterName = "$driveId";
                    _getCountDriveCommand.Parameters.Add(_getCountDriveParam1);
                    _getCountDriveParam1.Value = driveId.ToByteArray();
                    var count = _database.ExecuteNonQuery(conn, _getCountDriveCommand);
                    return count;
                } // using
        }

        // SELECT driveId,fileId,timeStamp
        public DriveCommandMessageQueueRecord ReadRecordFromReaderAll(SqliteDataReader rdr)
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
                    throw new Exception("Not a GUID in driveId...");
                item.driveId = new Guid(_guid);
            }

            if (rdr.IsDBNull(1))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                bytesRead = rdr.GetBytes(1, 0, _guid, 0, 16);
                if (bytesRead != 16)
                    throw new Exception("Not a GUID in fileId...");
                item.fileId = new Guid(_guid);
            }

            if (rdr.IsDBNull(2))
                throw new Exception("Impossible, item is null in DB, but set as NOT NULL");
            else
            {
                item.timeStamp = new UnixTimeUtc(rdr.GetInt64(2));
            }
            return item;
       }

        public int Delete(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
                using (var _delete0Command = _database.CreateCommand())
                {
                    _delete0Command.CommandText = "DELETE FROM driveCommandMessageQueue " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId";
                    var _delete0Param1 = _delete0Command.CreateParameter();
                    _delete0Param1.ParameterName = "$driveId";
                    _delete0Command.Parameters.Add(_delete0Param1);
                    var _delete0Param2 = _delete0Command.CreateParameter();
                    _delete0Param2.ParameterName = "$fileId";
                    _delete0Command.Parameters.Add(_delete0Param2);

                _delete0Param1.Value = driveId.ToByteArray();
                _delete0Param2.Value = fileId.ToByteArray();
                var count = _database.ExecuteNonQuery(conn, _delete0Command);
                return count;
                } // Using
        }

        public DriveCommandMessageQueueRecord ReadRecordFromReader0(SqliteDataReader rdr, Guid driveId,Guid fileId)
        {
            var result = new List<DriveCommandMessageQueueRecord>();
            byte[] _tmpbuf = new byte[65535+1];
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var _guid = new byte[16];
            var item = new DriveCommandMessageQueueRecord();
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

        public DriveCommandMessageQueueRecord Get(DatabaseBase.DatabaseConnection conn, Guid driveId,Guid fileId)
        {
                using (var _get0Command = _database.CreateCommand())
                {
                    _get0Command.CommandText = "SELECT timeStamp FROM driveCommandMessageQueue " +
                                                 "WHERE driveId = $driveId AND fileId = $fileId LIMIT 1;";
                    var _get0Param1 = _get0Command.CreateParameter();
                    _get0Param1.ParameterName = "$driveId";
                    _get0Command.Parameters.Add(_get0Param1);
                    var _get0Param2 = _get0Command.CreateParameter();
                    _get0Param2.ParameterName = "$fileId";
                    _get0Command.Parameters.Add(_get0Param2);

                _get0Param1.Value = driveId.ToByteArray();
                _get0Param2.Value = fileId.ToByteArray();
                    lock (conn._lock)
                    {
                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _get0Command, System.Data.CommandBehavior.SingleRow))
                {
                    if (!rdr.Read())
                    {
                        return null;
                    }
                    var r = ReadRecordFromReader0(rdr, driveId,fileId);
                    return r;
                } // using
            } // lock
            } // using
        }

    }
}
