using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveMainIndex : TableDriveMainIndexCRUD
    {
        private SqliteCommand _insertCommand = null;
        private static Object _insertLock = new Object();
        private SqliteParameter _insertParam1 = null;
        private SqliteParameter _insertParam2 = null;
        private SqliteParameter _insertParam3 = null;
        private SqliteParameter _insertParam4 = null;
        private SqliteParameter _insertParam5 = null;
        private SqliteParameter _insertParam6 = null;
        private SqliteParameter _insertParam7 = null;
        private SqliteParameter _insertParam8 = null;
        private SqliteParameter _insertParam9 = null;
        private SqliteParameter _insertParam10 = null;
        private SqliteParameter _insertParam11 = null;
        private SqliteParameter _insertParam12 = null;
        private SqliteParameter _insertParam13 = null;
        private SqliteParameter _insertParam14 = null;
        private SqliteParameter _insertParam15 = null;
        private SqliteParameter _insertParam16 = null;
        private SqliteParameter _insertParam17 = null;

        private SqliteCommand _updateCommand = null;
        private SqliteParameter _uparam1 = null;
        private SqliteParameter _uparam2 = null;
        private SqliteParameter _uparam3 = null;
        private SqliteParameter _uparam4 = null;
        private SqliteParameter _uparam5 = null;
        private SqliteParameter _uparam6 = null;
        private SqliteParameter _uparam7 = null;
        private SqliteParameter _uparam8 = null;
        private SqliteParameter _uparam9 = null;
        private SqliteParameter _uparam10 = null;
        private SqliteParameter _uparam11 = null;
        private SqliteParameter _uparam12 = null;
        private Object _updateLock = new Object();

        private SqliteCommand _touchCommand = null;
        private SqliteParameter _tparam1 = null;
        private SqliteParameter _tparam2 = null;
        private SqliteParameter _tparam3 = null;
        private Object _touchLock = new Object();

        private SqliteCommand _sizeCommand = null;
        private SqliteParameter _sparam1 = null;
        private Object _sizeLock = new Object();

        public TableDriveMainIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }


        ~TableDriveMainIndex()
        {
        }


        public override void Dispose()
        {
            _updateCommand?.Dispose();
            _updateCommand = null;

            _touchCommand?.Dispose();
            _touchCommand = null;

            _sizeCommand?.Dispose();
            _sizeCommand = null;

            base.Dispose();
            GC.SuppressFinalize(this);
        }


        public (Int64, Int64) GetDriveSize(DatabaseBase.DatabaseConnection conn, Guid driveId)
        {
            lock (_sizeLock)
            {
                // Make sure we only prep once 
                if (_sizeCommand == null)
                {
                    _sizeCommand = _database.CreateCommand(conn);

                    _sizeCommand.CommandText =
                        $"PRAGMA read_uncommitted = 1; SELECT count(*), sum(byteCount) FROM drivemainindex WHERE driveid = $driveId; PRAGMA read_uncommitted = 0;";

                    _sparam1 = _sizeCommand.CreateParameter();
                    _sparam1.ParameterName = "$driveId";
                    _sizeCommand.Parameters.Add(_sparam1);
                }

                _sparam1.Value = driveId.ToByteArray();

                using (SqliteDataReader rdr = _database.ExecuteReader(conn, _sizeCommand, System.Data.CommandBehavior.Default))
                {
                    if (rdr.Read())
                    {
                        long count = rdr.GetInt64(0);
                        long size  = rdr.GetInt64(1);
                        return (count, size);
                    }
                }
            }

            return (-1, -1);
        }


        /// <summary>
        /// For testing only. Updates the updatedTimestamp for the supplied item.
        /// </summary>
        /// <param name="fileId">Item to touch</param>
        public void TestTouch(DatabaseBase.DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            lock (_touchLock)
            {
                // Make sure we only prep once 
                if (_touchCommand == null)
                {
                    _touchCommand = _database.CreateCommand(conn);

                    _touchCommand.CommandText =
                        $"UPDATE drivemainindex SET modified=$modified WHERE driveId = $driveId AND fileid = $fileid;";

                    _tparam1 = _touchCommand.CreateParameter();
                    _tparam2 = _touchCommand.CreateParameter();
                    _tparam3 = _touchCommand.CreateParameter();

                    _tparam1.ParameterName = "$fileid";
                    _tparam2.ParameterName = "$modified";
                    _tparam3.ParameterName = "$driveId";

                    _touchCommand.Parameters.Add(_tparam1);
                    _touchCommand.Parameters.Add(_tparam2);
                    _touchCommand.Parameters.Add(_tparam3);
                }

                _tparam1.Value = fileId.ToByteArray();
                _tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _tparam3.Value = driveId.ToByteArray();

                _database.ExecuteNonQuery(conn, _touchCommand);
            }
        }

        // Delete when done with conversion from many DBs to unoDB
        public virtual int InsertRawTransfer(DatabaseBase.DatabaseConnection conn, DriveMainIndexRecord item)
        {
            lock (_insertLock)
            {
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand(conn);
                    _insertCommand.CommandText = "INSERT INTO driveMainIndex (driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,created,modified) " +
                                                 "VALUES ($driveId,$fileId,$globalTransitId,$fileState,$requiredSecurityGroup,$fileSystemType,$userDate,$fileType,$dataType,$archivalStatus,$historyStatus,$senderId,$groupId,$uniqueId,$byteCount,$created,$modified)";
                    _insertParam1 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam1);
                    _insertParam1.ParameterName = "$driveId";
                    _insertParam2 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam2);
                    _insertParam2.ParameterName = "$fileId";
                    _insertParam3 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam3);
                    _insertParam3.ParameterName = "$globalTransitId";
                    _insertParam4 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam4);
                    _insertParam4.ParameterName = "$fileState";
                    _insertParam5 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam5);
                    _insertParam5.ParameterName = "$requiredSecurityGroup";
                    _insertParam6 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam6);
                    _insertParam6.ParameterName = "$fileSystemType";
                    _insertParam7 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam7);
                    _insertParam7.ParameterName = "$userDate";
                    _insertParam8 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam8);
                    _insertParam8.ParameterName = "$fileType";
                    _insertParam9 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam9);
                    _insertParam9.ParameterName = "$dataType";
                    _insertParam10 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam10);
                    _insertParam10.ParameterName = "$archivalStatus";
                    _insertParam11 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam11);
                    _insertParam11.ParameterName = "$historyStatus";
                    _insertParam12 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam12);
                    _insertParam12.ParameterName = "$senderId";
                    _insertParam13 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam13);
                    _insertParam13.ParameterName = "$groupId";
                    _insertParam14 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam14);
                    _insertParam14.ParameterName = "$uniqueId";
                    _insertParam15 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam15);
                    _insertParam15.ParameterName = "$byteCount";
                    _insertParam16 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam16);
                    _insertParam16.ParameterName = "$created";
                    _insertParam17 = _insertCommand.CreateParameter();
                    _insertCommand.Parameters.Add(_insertParam17);
                    _insertParam17.ParameterName = "$modified";
                    _insertCommand.Prepare();
                }
                _insertParam1.Value = item.driveId.ToByteArray();
                _insertParam2.Value = item.fileId.ToByteArray();
                _insertParam3.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam4.Value = item.fileState;
                _insertParam5.Value = item.requiredSecurityGroup;
                _insertParam6.Value = item.fileSystemType;
                _insertParam7.Value = item.userDate.milliseconds;
                _insertParam8.Value = item.fileType;
                _insertParam9.Value = item.dataType;
                _insertParam10.Value = item.archivalStatus;
                _insertParam11.Value = item.historyStatus;
                _insertParam12.Value = item.senderId ?? (object)DBNull.Value;
                _insertParam13.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam14.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _insertParam15.Value = item.byteCount;
                var now = UnixTimeUtcUnique.Now();
                // HAND HACK FOR CONVERSION
                _insertParam16.Value = item.created.uniqueTime;
                _insertParam17.Value = item.modified?.uniqueTime ?? (object)DBNull.Value;
                var count = _database.ExecuteNonQuery(conn, _insertCommand);
                item.modified = null;
                // HAND HACK END
                if (count > 0)
                {
                    item.created = now;
                }
                return count;
            } // Lock
        }


        public override int Update(DatabaseBase.DatabaseConnection conn, DriveMainIndexRecord item)
        {
            throw new Exception("can't use");
        }

        // It is not allowed to update the GlobalTransitId
        public void UpdateRow(DatabaseBase.DatabaseConnection conn, Guid driveId, 
            Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            IdentityDatabase.NullableGuid nullableUniqueId = null,
            Int32? archivalStatus = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null,
            Int64? byteCount = null)
        {
            lock (_updateLock)
            {
                // Make sure we only prep once 
                if (_updateCommand == null)
                {
                    _updateCommand = _database.CreateCommand(conn);
                    _uparam1 = _updateCommand.CreateParameter();
                    _uparam2 = _updateCommand.CreateParameter();
                    _uparam3 = _updateCommand.CreateParameter();
                    _uparam4 = _updateCommand.CreateParameter();
                    _uparam5 = _updateCommand.CreateParameter();
                    _uparam6 = _updateCommand.CreateParameter();
                    _uparam7 = _updateCommand.CreateParameter();
                    _uparam8 = _updateCommand.CreateParameter();
                    _uparam9 = _updateCommand.CreateParameter();
                    _uparam10 = _updateCommand.CreateParameter();
                    _uparam11 = _updateCommand.CreateParameter();
                    _uparam12 = _updateCommand.CreateParameter();

                    _uparam1.ParameterName = "$modified";
                    _uparam2.ParameterName = "$filetype";
                    _uparam3.ParameterName = "$datatype";
                    _uparam4.ParameterName = "$senderid";
                    _uparam5.ParameterName = "$groupid";
                    _uparam6.ParameterName = "$uniqueid";
                    _uparam7.ParameterName = "$userdate";
                    _uparam8.ParameterName = "$requiredSecurityGroup";
                    _uparam9.ParameterName = "$globaltransitid";
                    _uparam10.ParameterName = "$archivalStatus";
                    _uparam11.ParameterName = "$fileState";
                    _uparam12.ParameterName = "$byteCount";

                    _updateCommand.Parameters.Add(_uparam1);
                    _updateCommand.Parameters.Add(_uparam2);
                    _updateCommand.Parameters.Add(_uparam3);
                    _updateCommand.Parameters.Add(_uparam4);
                    _updateCommand.Parameters.Add(_uparam5);
                    _updateCommand.Parameters.Add(_uparam6);
                    _updateCommand.Parameters.Add(_uparam7);
                    _updateCommand.Parameters.Add(_uparam8);
                    _updateCommand.Parameters.Add(_uparam9);
                    _updateCommand.Parameters.Add(_uparam10);
                    _updateCommand.Parameters.Add(_uparam11);
                    _updateCommand.Parameters.Add(_uparam12);
                }

                string stm;

                stm = "modified = $modified";


                if (fileType != null)
                    stm += ", filetype = $filetype ";

                if (dataType != null)
                    stm += ", datatype = $datatype ";

                if (senderId != null)
                    stm += ", senderid = $senderid ";

                if (groupId != null)
                    stm += ", groupid = $groupid ";

                // Note: Todd removed this null check since we must be able to set the uniqueId to null when a file is deleted
                if (nullableUniqueId != null)
                    stm += ", uniqueid = $uniqueid ";

                if (globalTransitId != null)
                    stm += ", globaltransitid = $globaltransitid ";

                if (userDate != null)
                    stm += ", userdate = $userdate ";

                if (requiredSecurityGroup != null)
                    stm += ", requiredSecurityGroup = $requiredSecurityGroup ";

                if (archivalStatus != null)
                    stm += ", archivalStatus = $archivalStatus";

                if (fileState != null)
                    stm += ", fileState = $fileState";

                if (byteCount != null)
                {
                    if (byteCount < 1)
                        throw new ArgumentException("byteCount must be at least 1");
                    stm += ", byteCount = $byteCount";
                }

                _updateCommand.CommandText =
                    $"UPDATE drivemainindex SET " + stm + $" WHERE driveid = x'{Convert.ToHexString(driveId.ToByteArray())}' AND fileid = x'{Convert.ToHexString(fileId.ToByteArray())}'";

                _uparam1.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _uparam2.Value = fileType ?? (object)DBNull.Value;
                _uparam3.Value = dataType ?? (object)DBNull.Value;
                _uparam4.Value = senderId ?? (object)DBNull.Value;
                _uparam5.Value = groupId?.ToByteArray() ?? (object)DBNull.Value;
                if (nullableUniqueId == null)
                    _uparam6.Value = (object)DBNull.Value;
                else
                    _uparam6.Value = nullableUniqueId.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam7.Value = userDate?.milliseconds ?? (object)DBNull.Value;
                _uparam8.Value = requiredSecurityGroup ?? (object)DBNull.Value;
                _uparam9.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam10.Value = archivalStatus ?? (object)DBNull.Value;
                _uparam11.Value = fileState ?? (object)DBNull.Value;
                _uparam12.Value = byteCount ?? (object)DBNull.Value;

                _database.ExecuteNonQuery(conn, _updateCommand);
            }
        }
    }
}