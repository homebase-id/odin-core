using System;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class TableMainIndexData
    {
        public Guid FileId;
        public Guid? GlobalTransitId;
        public UnixTimeUtc CreatedTimeStamp; // We might want to delete this parameter?
        public UnixTimeUtcUnique UpdatedTimeStamp;
        public Int32 FileType;
        public Int32 DataType;
        public byte[] SenderId;
        public Guid? GroupId;
        public Guid? uniqueId;
        public UInt64 UserDate; // Need to create a unique struct data type for this
        public bool IsArchived;
        public bool IsHistory;
        public Int32 RequiredSecurityGroup;
    };

    public class TableMainIndex : TableBase
    {
        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _param1 = null;
        private SQLiteParameter _param2 = null;
        private SQLiteParameter _param3 = null;
        private SQLiteParameter _param4 = null;
        private SQLiteParameter _param5 = null;
        private SQLiteParameter _param6 = null;
        private SQLiteParameter _param7 = null;
        private SQLiteParameter _param8 = null;
        private SQLiteParameter _param9 = null;
        private SQLiteParameter _param10 = null;
        private SQLiteParameter _param11 = null;
        private SQLiteParameter _param12 = null;
        private SQLiteParameter _param13 = null;
        private SQLiteParameter _param14 = null;
        private Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private Object _deleteLock = new Object();

        private SQLiteCommand _updateCommand = null;
        private SQLiteParameter _uparam1 = null;
        private SQLiteParameter _uparam2 = null;
        private SQLiteParameter _uparam3 = null;
        private SQLiteParameter _uparam4 = null;
        private SQLiteParameter _uparam5 = null;
        private SQLiteParameter _uparam6 = null;
        private SQLiteParameter _uparam7 = null;
        private SQLiteParameter _uparam8 = null;
        private SQLiteParameter _uparam9 = null;
        private SQLiteParameter _uparam10 = null;
        private Object _updateLock = new Object();

        private SQLiteCommand _touchCommand = null;
        private SQLiteParameter _tparam1 = null;
        private SQLiteParameter _tparam2 = null;
        private Object _touchLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private Object _selectLock = new Object();

        public TableMainIndex(DriveDatabase db) : base(db)
        {
        }


        ~TableMainIndex()
        {
        }


        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _updateCommand?.Dispose();
            _updateCommand = null;

            _touchCommand?.Dispose();
            _touchCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;
        }


        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if(dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS mainindex;";
                    cmd.ExecuteNonQuery();
                }
                
                cmd.CommandText =
                    @"CREATE TABLE if not exists mainindex(
                     fileid BLOB UNIQUE PRIMARY KEY NOT NULL,
                     globaltransitid BLOB,
                     createdtimestamp INTEGER NOT NULL,
                     updatedtimestamp INTEGER UNIQUE,
                     userdate INTEGER NOT NULL,
                     filetype INTEGER NOT NULL,
                     datatype INTEGER NOT NULL,
                     isArchived INTEGER NOT NULL,
                     isHistory INTEGER NOT NULL,
                     senderid BLOB,
                     groupId BLOB,
                     uniqueid BLOB,
                     requiredSecurityGroup INTEGER NOT NULL,
                     fileSystemType INTEGER NOT NULL); "
                    + "CREATE INDEX if not exists idxupdatedtimestamp ON mainindex(updatedtimestamp); "
                    + "CREATE INDEX if not exists idxglobaltransitid ON mainindex(globaltransitid);";

                cmd.ExecuteNonQuery();
            }
        }

        // Presently only needed for testing / validation. Not expecting we should need this
        // Runtime.
        public TableMainIndexData Get(Guid fileId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT createdtimestamp, updatedtimestamp, filetype, datatype, senderid, groupId, userdate, isArchived, isHistory, requiredSecurityGroup, globaltransitid, uniqueid FROM mainindex WHERE fileid=$fileid";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$fileid";
                    _selectCommand.Parameters.Add(_sparam1);
                }

                _sparam1.Value = fileId;
                var GroupId = new byte[16];

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleResult))
                {
                    TableMainIndexData md = new TableMainIndexData();

                    int i = 0;
                    while (rdr.Read())
                    {
                        md.FileId = fileId;
                        md.CreatedTimeStamp = new UnixTimeUtc((UInt64)rdr.GetInt64(0));

                        // type = rdr.GetDataTypeName(1);
                        if (!rdr.IsDBNull(1))
                            md.UpdatedTimeStamp = new UnixTimeUtcUnique((UInt64) rdr.GetInt64(1));

                        md.FileType = rdr.GetInt32(2);
                        md.DataType = rdr.GetInt32(3);
                        if (!rdr.IsDBNull(4))
                        {
                            md.SenderId = new byte[16];
                            rdr.GetBytes(4, 0, md.SenderId, 0, 16);
                        }

                        if (!rdr.IsDBNull(5))
                        {
                            rdr.GetBytes(5, 0, GroupId, 0, 16);
                            md.GroupId = new Guid(GroupId);
                        }

                        md.UserDate = (UInt64)rdr.GetInt64(6);
                        md.IsArchived = rdr.GetBoolean(7);
                        md.IsHistory = rdr.GetBoolean(8);
                        md.RequiredSecurityGroup = rdr.GetInt32(9);

                        if (rdr.IsDBNull(10))
                        {
                            md.GlobalTransitId = null;
                        }
                        else
                        {
                            var ba = new byte[16];
                            rdr.GetBytes(10, 0, ba, 0, 16);
                            md.GlobalTransitId = new Guid(ba);
                        }

                        if (rdr.IsDBNull(11))
                        {
                            md.uniqueId = null;
                        }
                        else
                        {
                            var ui = new byte[16];
                            rdr.GetBytes(11, 0, ui, 0, 16);
                            md.uniqueId = new Guid(ui);
                        }

                        i++;
                    }

                    if (i == 0)
                        return null;

                    if (i > 1)
                        throw new Exception("DB-Boom");

                    return md;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileId"></param>
        /// <param name="createdUnixTimeUtc">File creation timestamp - might be redundant (fileId has it)</param>
        /// <param name="fileType"></param>
        /// <param name="dataType"></param>
        /// <param name="SenderId"></param>
        /// <param name="GroupId"></param>
        /// <param name="userZeroSeconds">user defined date in GetZeroSeconds()</param>
        /// <param name="isArchived"></param>
        /// <param name="isHistory"></param>
        /// <param name="requiredSecurityGroup"></param>
        public void InsertRow(Guid fileId,
                                Guid? globalTransitId,
                                UnixTimeUtc createdUnixTimeUtc,
                                Int32 fileType,
                                Int32 dataType,
                                byte[] SenderId,
                                Guid? GroupId,
                                Guid? UniqueId,
                                UInt64 userZeroSeconds,
                                bool isArchived,
                                bool isHistory,
                                Int32 requiredSecurityGroup,
                                Int32 fileSystemType = 0)
        {
            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText =
                        @"INSERT INTO mainindex(
                            fileid, 
                            globaltransitid, 
                            createdtimestamp,
                            updatedtimestamp,
                            isarchived,
                            ishistory,
                            filetype,
                            datatype,
                            senderid,
                            groupid,
                            uniqueid,
                            userdate,
                            requiredSecurityGroup,
                            fileSystemType)
                    VALUES ($fileid, $globaltransitid, $createdtimestamp, $updatedtimestamp,
                        $isarchived, $ishistory,
                        $filetype, $datatype,
                        $senderid, $groupid, $uniqueid, $userdate, $requiredSecurityGroup,
                        $fileSystemType)";

                    _param1 = _insertCommand.CreateParameter();
                    _param1.ParameterName = "$fileid";
                    _param2 = _insertCommand.CreateParameter();
                    _param2.ParameterName = "$globaltransitid";
                    _param3 = _insertCommand.CreateParameter();
                    _param3.ParameterName = "$createdtimestamp";
                    _param4 = _insertCommand.CreateParameter();
                    _param4.ParameterName = "$updatedtimestamp";
                    _param5 = _insertCommand.CreateParameter();
                    _param5.ParameterName = "$isarchived";
                    _param6 = _insertCommand.CreateParameter();
                    _param6.ParameterName = "$ishistory";
                    _param7 = _insertCommand.CreateParameter();
                    _param7.ParameterName = "$filetype";
                    _param8 = _insertCommand.CreateParameter();
                    _param8.ParameterName = "$datatype";
                    _param9 = _insertCommand.CreateParameter();
                    _param9.ParameterName = "$senderid";
                    _param10 = _insertCommand.CreateParameter();
                    _param10.ParameterName = "$groupid";
                    _param11 = _insertCommand.CreateParameter();
                    _param11.ParameterName = "$uniqueid";
                    _param12 = _insertCommand.CreateParameter();
                    _param12.ParameterName = "$userdate";
                    _param13 = _insertCommand.CreateParameter();
                    _param13.ParameterName = "$requiredSecurityGroup";

                    _param14 = _insertCommand.CreateParameter();
                    _param14.ParameterName = "$fileSystemType";
                    
                    _insertCommand.Parameters.Add(_param1);
                    _insertCommand.Parameters.Add(_param2);
                    _insertCommand.Parameters.Add(_param3);
                    _insertCommand.Parameters.Add(_param4);
                    _insertCommand.Parameters.Add(_param5);
                    _insertCommand.Parameters.Add(_param6);
                    _insertCommand.Parameters.Add(_param7);
                    _insertCommand.Parameters.Add(_param8);
                    _insertCommand.Parameters.Add(_param9);
                    _insertCommand.Parameters.Add(_param10);
                    _insertCommand.Parameters.Add(_param11);
                    _insertCommand.Parameters.Add(_param12);
                    _insertCommand.Parameters.Add(_param13);
                    _insertCommand.Parameters.Add(_param14);
                }

                _param1.Value = fileId;
                _param2.Value = globalTransitId;
                _param3.Value = createdUnixTimeUtc.milliseconds;
                _param4.Value = null;
                _param5.Value = isArchived;
                _param6.Value = isHistory;
                _param7.Value = fileType;
                _param8.Value = dataType;
                _param9.Value = SenderId;
                _param10.Value = GroupId;
                _param11.Value = UniqueId;
                _param12.Value = userZeroSeconds;
                _param13.Value = requiredSecurityGroup;
                _param14.Value = fileSystemType;

                _database.BeginTransaction();
                _insertCommand.ExecuteNonQuery();
            }
        }

        public void DeleteRow(Guid fileId)
        {
            lock (_deleteLock)
            {
                // Make sure we only prep once 
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM mainindex WHERE fileid=$fileid";

                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$fileid";
                    _deleteCommand.Parameters.Add(_dparam1);
                }

                _dparam1.Value = fileId;

                _database.BeginTransaction();
                _deleteCommand.ExecuteNonQuery();
            }
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
                        $"UPDATE mainindex SET updatedtimestamp=$updatedtimestamp WHERE fileid = $fileid;";

                    _tparam1 = _touchCommand.CreateParameter();
                    _tparam1.ParameterName = "$fileid";
                    _touchCommand.Parameters.Add(_tparam1);

                    _tparam2 = _touchCommand.CreateParameter();
                    _tparam2.ParameterName = "$updatedtimestamp";
                    _touchCommand.Parameters.Add(_tparam2);
                }

                _tparam1.Value = fileId;
                _tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;

                _database.BeginTransaction();
                _touchCommand.ExecuteNonQuery();
            }
        }

        // It is not allowed to update the GlobalTransitId
        public void UpdateRow(Guid fileId,
            Guid? globalTransitId = null,
            Int32? fileType = null,
            Int32? dataType = null,
            byte[] senderId = null,
            Guid? groupId = null,
            Guid? uniqueId = null,
            UInt64? userDate = null,
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

                    _uparam1.ParameterName = "$updatedtimestamp";
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

                stm = "updatedtimestamp = $updatedtimestamp ";


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
                _uparam2.Value = fileType;
                _uparam3.Value = dataType;
                _uparam4.Value = senderId;
                _uparam5.Value = groupId;
                _uparam6.Value = uniqueId;
                _uparam7.Value = userDate;
                _uparam8.Value = requiredSecurityGroup;
                _uparam9.Value = globalTransitId;
                // _uparam10.Value = fileSystemType;
                _database.BeginTransaction();
                _updateCommand.ExecuteNonQuery();
            }
        }
    }
}