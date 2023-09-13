using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;

namespace Odin.Core.Storage.SQLite.DriveDatabase
{
    public class TableMainIndex : TableMainIndexCRUD
    {
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
        private Object _touchLock = new Object();

        public TableMainIndex(DriveDatabase db, CacheHelper cache) : base(db, cache)
        {
        }


        ~TableMainIndex()
        {
        }


        public override void Dispose()
        {
            _updateCommand?.Dispose();
            _updateCommand = null;

            _touchCommand?.Dispose();
            _touchCommand = null;

            base.Dispose();
        }


        /// <summary>
        /// For testing only. Updates the updatedTimestamp for the supplied item.
        /// </summary>
        /// <param name="fileId">Item to touch</param>
        public void TestTouch(Guid fileId)
        {
            lock (_touchLock)
            {
                // Make sure we only prep once 
                if (_touchCommand == null)
                {
                    _touchCommand = _database.CreateCommand();

                    _touchCommand.CommandText =
                        $"UPDATE mainindex SET modified=$modified WHERE fileid = $fileid;";

                    _tparam1 = _touchCommand.CreateParameter();
                    _tparam1.ParameterName = "$fileid";
                    _touchCommand.Parameters.Add(_tparam1);

                    _tparam2 = _touchCommand.CreateParameter();
                    _tparam2.ParameterName = "$modified";
                    _touchCommand.Parameters.Add(_tparam2);
                }

                _tparam1.Value = fileId.ToByteArray();
                _tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;

                _database.ExecuteNonQuery(_touchCommand);
            }
        }

        public override int Update(MainIndexRecord item)
        {
            throw new Exception("can't use");
        }

        // It is not allowed to update the GlobalTransitId
        public void UpdateRow(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileState = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
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
                    _updateCommand = _database.CreateCommand();
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

                    _uparam1.ParameterName = "modified";
                    _updateCommand.Parameters.Add(_uparam1);

                    _uparam2.ParameterName = "$filetype";
                    _updateCommand.Parameters.Add(_uparam2);

                    _uparam3.ParameterName = "$datatype";
                    _updateCommand.Parameters.Add(_uparam3);

                    _uparam4.ParameterName = "$senderid";
                    _updateCommand.Parameters.Add(_uparam4);

                    _uparam5.ParameterName = "$groupid";
                    _updateCommand.Parameters.Add(_uparam5);

                    _uparam6.ParameterName = "$uniqueid";
                    _updateCommand.Parameters.Add(_uparam6);

                    _uparam7.ParameterName = "$userdate";
                    _updateCommand.Parameters.Add(_uparam7);

                    _uparam8.ParameterName = "$requiredSecurityGroup";
                    _updateCommand.Parameters.Add(_uparam8);

                    _uparam9.ParameterName = "$globaltransitid";
                    _updateCommand.Parameters.Add(_uparam9);

                    _uparam10.ParameterName = "$archivalStatus";
                    _updateCommand.Parameters.Add(_uparam10);

                    _uparam11.ParameterName = "$fileState";
                    _updateCommand.Parameters.Add(_uparam11);

                    _uparam12.ParameterName = "$byteCount";
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
                // if (uniqueId != null)
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
                    stm += ", byteCount = $byteCount";

                _updateCommand.CommandText =
                    $"UPDATE mainindex SET " + stm + $" WHERE fileid = x'{Convert.ToHexString(fileId.ToByteArray())}'";

                _uparam1.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _uparam2.Value = fileType ?? (object)DBNull.Value;
                _uparam3.Value = dataType ?? (object)DBNull.Value;
                _uparam4.Value = senderId ?? (object)DBNull.Value;
                _uparam5.Value = groupId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam6.Value = uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam7.Value = userDate?.milliseconds ?? (object)DBNull.Value;
                _uparam8.Value = requiredSecurityGroup ?? (object)DBNull.Value;
                _uparam9.Value = globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _uparam10.Value = archivalStatus ?? (object)DBNull.Value;
                _uparam11.Value = fileState ?? (object)DBNull.Value;
                _uparam12.Value = byteCount ?? (object)DBNull.Value;

                _database.ExecuteNonQuery(_updateCommand);
            }
        }
    }
}