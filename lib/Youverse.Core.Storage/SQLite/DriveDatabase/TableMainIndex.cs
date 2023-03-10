using System;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
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
        private Object _updateLock = new Object();

        private SqliteCommand _touchCommand = null;
        private SqliteParameter _tparam1 = null;
        private SqliteParameter _tparam2 = null;
        private Object _touchLock = new Object();

        public TableMainIndex(DriveDatabase db) : base(db)
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

                _database.BeginTransaction();
                _touchCommand.ExecuteNonQuery(_database);
            }
        }

        public override int Update(MainIndexRecord item)
        {
            throw new Exception("can't use");
        }

        // It is not allowed to update the GlobalTransitId
        public void UpdateRow(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            UnixTimeUtc? userDate = null,
            Int32? requiredSecurityGroup = null)
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

                    // _uparam10.ParameterName = "$fileSystemType";
                    // _updateCommand.Parameters.Add(_uparam10);
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

                if (uniqueId != null)
                    stm += ", uniqueid = $uniqueid ";

                if (globalTransitId != null)
                    stm += ", globaltransitid = $globaltransitid ";

                if (userDate != null)
                    stm += ", userdate = $userdate ";

                if (requiredSecurityGroup != null)
                    stm += ", requiredSecurityGroup = $requiredSecurityGroup ";

                // if (fileSystemType != null)
                //     stm += ", fileSystemType = $fileSystemType";

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
                // _uparam10.Value = fileSystemType;
                _database.BeginTransaction();
                _updateCommand.ExecuteNonQuery(_database);
            }
        }
    }
}