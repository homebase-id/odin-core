using System;
using System.Data.SQLite;

namespace Youverse.Core.Services.Drive.Query.Sqlite
{
    public class TableMainIndexData
    {
        public Guid fileId;
        public UInt64 createdTimeStamp;
        public UInt64 updatedTimeStamp;
        public Int32 fileType;
        public Int32 dataType;
        public byte[] SenderId;
        public byte[] ThreadId;
        public UInt64 userDate;
        public bool isArchived;
        public bool isHistory;
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
        private static Object _insertLock = new Object();

        private SQLiteCommand _updateCommand = null;
        private SQLiteParameter _uparam1 = null;
        private SQLiteParameter _uparam2 = null;
        private SQLiteParameter _uparam3 = null;
        private SQLiteParameter _uparam4 = null;
        private SQLiteParameter _uparam5 = null;
        private SQLiteParameter _uparam6 = null;
        private static Object _updateLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private static Object _selectLock = new Object();

        public TableMainIndex(DriveIndexDatabase db) : base(db)
        {
        }

        ~TableMainIndex()
        {
            if (_insertCommand != null)
            {
                _insertCommand.Dispose();
                _insertCommand = null;
            }

            if (_updateCommand != null)
            {
                _updateCommand.Dispose();
                _updateCommand = null;
            }

            if (_selectCommand != null)
            {
                _selectCommand.Dispose();
                _selectCommand = null;
            }
        }

        public override void CreateTable()
        {
            using (var cmd = _driveIndexDatabase.CreateCommand())
            {
                // cmd.CommandText = "DROP TABLE IF EXISTS mainindex;";
                // cmd.ExecuteNonQuery();

                cmd.CommandText =
                    @"CREATE TABLE if not exists mainindex(
                     fileid BLOB UNIQUE PRIMARY KEY NOT NULL,
                     createdtimestamp INTEGER NOT NULL,
                     updatedtimestamp INTEGER,
                     userdate INTEGER NOT NULL,
                     filetype INTEGER NOT NULL,
                     datatype INTEGER NOT NULL,
                     isArchived INTEGER NOT NULL,
                     isHistory INTEGER NOT NULL,
                     senderid BLOB,
                     threadId BLOB); "
                    + "CREATE INDEX idxupdatedtimestamp ON mainindex(updatedtimestamp);";

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
                    _selectCommand = _driveIndexDatabase.CreateCommand();
                    _selectCommand.CommandText =
                        $"SELECT createdtimestamp, updatedtimestamp, filetype, datatype, senderid, threadId, userdate, isArchived, isHistory FROM mainindex WHERE fileid=$fileid";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$fileid";
                    _selectCommand.Parameters.Add(_sparam1);
                }

                _sparam1.Value = fileId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleResult))
                {
                    TableMainIndexData md = new TableMainIndexData();

                    int i = 0;
                    while (rdr.Read())
                    {
                        md.fileId = fileId;
                        md.createdTimeStamp = (UInt64) rdr.GetInt64(0);

                        // type = rdr.GetDataTypeName(1);
                        if (!rdr.IsDBNull(1))
                            md.updatedTimeStamp = (UInt64)rdr.GetInt64(1);

                        md.fileType = rdr.GetInt32(2);
                        md.dataType = rdr.GetInt32(3);

                        if (!rdr.IsDBNull(4))
                        {
                            md.SenderId = new byte[16];
                            rdr.GetBytes(4, 0, md.SenderId, 0, 16);
                        }

                        if (!rdr.IsDBNull(5))
                        {
                            md.ThreadId = new byte[16];
                            rdr.GetBytes(5, 0, md.ThreadId, 0, 16);
                        }

                        md.userDate = (UInt64)rdr.GetInt64(6);
                        md.isArchived = rdr.GetBoolean(7);
                        md.isHistory = rdr.GetBoolean(8);

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
        /// <param name="createdZeroSeconds">File creation timestamp in zero seconds</param>
        /// <param name="fileType"></param>
        /// <param name="dataType"></param>
        /// <param name="SenderId"></param>
        /// <param name="ThreadId"></param>
        /// <param name="userZeroSeconds">user defined date in GetZeroSeconds()</param>
        /// <param name="isArchived"></param>
        /// <param name="isHistory"></param>
        public void InsertRow(Guid fileId, 
                            UInt64 createdZeroSeconds,
                            Int32 fileType,
                            Int32 dataType,
                            byte[] SenderId, 
                            byte[] ThreadId, 
                            UInt64 userZeroSeconds,
                            bool isArchived, 
                            bool isHistory)
        {
            lock (_insertLock)
            {
                // Make sure we only prep once 
                if (_insertCommand == null)
                {
                    _insertCommand = _driveIndexDatabase.CreateCommand();
                    _insertCommand.CommandText =
                        @"INSERT INTO mainindex(
                        fileid, createdtimestamp, updatedtimestamp,
                        isarchived, ishistory,
                        filetype,
                        datatype,
                        senderid, threadid,
                        userdate)
                    VALUES ($fileid, $createdtimestamp, $updatedtimestamp,
                        $isarchived, $ishistory,
                        $filetype, $datatype,
                        $senderid, $threadid, $userdate)";

                    _param1 = _insertCommand.CreateParameter();
                    _param1.ParameterName = "$fileid";
                    _param2 = _insertCommand.CreateParameter();
                    _param2.ParameterName = "$createdtimestamp";
                    _param3 = _insertCommand.CreateParameter();
                    _param3.ParameterName = "$updatedtimestamp";
                    _param4 = _insertCommand.CreateParameter();
                    _param4.ParameterName = "$isarchived";
                    _param5 = _insertCommand.CreateParameter();
                    _param5.ParameterName = "$ishistory";
                    _param6 = _insertCommand.CreateParameter();
                    _param6.ParameterName = "$filetype";
                    _param7 = _insertCommand.CreateParameter();
                    _param7.ParameterName = "$datatype";
                    _param8 = _insertCommand.CreateParameter();
                    _param8.ParameterName = "$senderid";
                    _param9 = _insertCommand.CreateParameter();
                    _param9.ParameterName = "$threadid";
                    _param10 = _insertCommand.CreateParameter();
                    _param10.ParameterName = "$userdate";

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
                }

                _param1.Value = fileId;
                _param2.Value = createdZeroSeconds;
                // _param3.Value = null;
                _param3.Value = UnixTime.UnixTimeMillisecondsUnique();
                _param4.Value = isArchived;
                _param5.Value = isHistory;
                _param6.Value = fileType;
                _param7.Value = dataType;
                _param8.Value = SenderId;
                _param9.Value = ThreadId;
                _param10.Value = userZeroSeconds;

                _insertCommand.ExecuteNonQuery();
            }
        }

        public void UpdateRow(Guid fileId,
                              Int32? FileType = null,
                              Int32? DataType = null,
                              byte[] SenderId = null,
                              byte[] ThreadId = null,
                              UInt64? UserDate = null)
        {
            lock (_updateLock)
            {
                // Make sure we only prep once 
                if (_updateCommand == null)
                {
                    _updateCommand = _driveIndexDatabase.CreateCommand();
                    _uparam1 = _updateCommand.CreateParameter();
                    _uparam2 = _updateCommand.CreateParameter();
                    _uparam3 = _updateCommand.CreateParameter();
                    _uparam4 = _updateCommand.CreateParameter();
                    _uparam5 = _updateCommand.CreateParameter();
                    _uparam6 = _updateCommand.CreateParameter();

                    _uparam1.ParameterName = "$updatedtimestamp";
                    _updateCommand.Parameters.Add(_uparam1);

                    _uparam2.ParameterName = "$filetype";
                    _updateCommand.Parameters.Add(_uparam2);

                    _uparam3.ParameterName = "$datatype";
                    _updateCommand.Parameters.Add(_uparam3);

                    _uparam4.ParameterName = "$senderid";
                    _updateCommand.Parameters.Add(_uparam4);

                    _uparam5.ParameterName = "$threadid";
                    _updateCommand.Parameters.Add(_uparam5);

                    _uparam6.ParameterName = "$userdate";
                    _updateCommand.Parameters.Add(_uparam6);
                }

                string stm;

                stm = "updatedtimestamp = $updatedtimestamp ";


                if (FileType != null)
                    stm += ", filetype = $filetype ";

                if (DataType != null)
                    stm += ", datatype = $datatype ";

                if (SenderId != null)
                    stm += ", senderid = $senderid ";

                if (ThreadId != null)
                    stm += ", threadid = $threadid ";

                if (UserDate != null)
                    stm += ", userdate = $userdate ";

                _updateCommand.CommandText =
                    $"UPDATE mainindex SET " + stm + $" WHERE fileid = x'{Convert.ToHexString(fileId.ToByteArray())}'";

                _uparam1.Value = UnixTime.UnixTimeMillisecondsUnique();
                _uparam2.Value = FileType;
                _uparam3.Value = DataType;
                _uparam4.Value = SenderId;
                _uparam5.Value = ThreadId;
                _uparam6.Value = UserDate;

                _updateCommand.ExecuteNonQuery();
            }
        }
    }
}   