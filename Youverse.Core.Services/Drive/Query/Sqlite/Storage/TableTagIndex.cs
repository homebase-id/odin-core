using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;

namespace Youverse.Core.Services.Drive.Query.Sqlite.Storage
{
    public class TableTagIndex : TableBase
    {
        private SQLiteCommand _insertCommand = null;
        private SQLiteParameter _iparam1 = null;
        private SQLiteParameter _iparam2 = null;
        private Object _insertLock = new Object();

        private SQLiteCommand _deleteCommand = null;
        private SQLiteParameter _dparam1 = null;
        private SQLiteParameter _dparam2 = null;
        private Object _deleteLock = new Object();

        private SQLiteCommand _deleteAllCommand = null;
        private SQLiteParameter _dallparam1 = null;
        private Object _deleteAllLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private Object _selectLock = new Object();

        public TableTagIndex(DriveIndexDatabase db) : base(db)
        {
        }

        ~TableTagIndex()
        {
            if (_insertCommand != null)
            {
                _insertCommand.Dispose();
                _insertCommand = null;
            }

            if (_deleteCommand != null)
            {
                _deleteCommand.Dispose();
                _deleteCommand = null;
            }

            if (_deleteAllCommand != null)
            {
                _deleteAllCommand.Dispose();
                _deleteAllCommand = null;
            }

            if (_selectCommand != null)
            {
                _selectCommand.Dispose();
                _selectCommand = null;
            }
        }

        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _driveIndexDatabase.CreateCommand())
            {
                if(dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS tagindex;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText = @"CREATE TABLE if not exists tagindex(fileid BLOB NOT NULL, tagid BLOB NOT NULL, UNIQUE(fileid,tagid));"
                                  + "CREATE INDEX TagIdx ON tagindex(tagid);";

                cmd.ExecuteNonQuery();
            }
        }

        // Hm is it better to return an empty List<Guid> rather than null for an empty set?
        public List<Guid> Get(Guid fileId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _driveIndexDatabase.CreateCommand();
                    _selectCommand.CommandText = @"SELECT tagid FROM tagindex WHERE fileid=$fileid";
                    _sparam1 = _selectCommand.CreateParameter();
                    _sparam1.ParameterName = "$fileid";
                    _selectCommand.Parameters.Add(_sparam1);
                }

                _sparam1.Value = fileId;

                using (SQLiteDataReader rdr = _selectCommand.ExecuteReader(System.Data.CommandBehavior.SingleResult))
                {
                    int i = 0;
                    List<Guid> acl = new List<Guid>();
                    byte[] bytes = new byte[16];

                    while (rdr.Read())
                    {
                        rdr.GetBytes(0, 0, bytes, 0, 16);
                        acl.Add(new Guid(bytes));
                        i++;
                    }

                    if (i < 1)
                        return null;
                    else
                        return acl;
                }
            }
        }

        public void InsertRows(Guid FileId, List<Guid> TagIdList)
        {
            if (TagIdList == null)
                return;

            lock (_insertLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_insertCommand == null)
                {
                    _insertCommand = _driveIndexDatabase.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO tagindex(fileid, tagid) VALUES($fileid, $tagid)";
                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$fileid";
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$tagid";
                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                }

                _iparam1.Value = FileId;
                for (int i = 0; i < TagIdList.Count; i++)
                {
                    _iparam2.Value = TagIdList[i].ToByteArray();
                   int rows =  _insertCommand.ExecuteNonQuery();
                   Debugger.Launch();
                }
            }
        }

        public void DeleteRow(Guid FileId, List<Guid> TagIdList)
        {
            if (TagIdList == null)
                return;

            lock (_deleteLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_deleteCommand == null)
                {
                    _deleteCommand = _driveIndexDatabase.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM tagindex WHERE fileid=$fileid AND tagid=$tagid";
                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$fileid";
                    _dparam2 = _deleteCommand.CreateParameter();
                    _dparam2.ParameterName = "$tagid";
                    _deleteCommand.Parameters.Add(_dparam1);
                    _deleteCommand.Parameters.Add(_dparam2);
                }

                for (int i = 0; i < TagIdList.Count; i++)
                {
                    _dparam1.Value = FileId;
                    _dparam2.Value = TagIdList[i].ToByteArray();
                    _deleteCommand.ExecuteNonQuery();
                }
            }
        }

        public void DeleteAllRows(Guid FileId)
        {
            lock (_deleteAllLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_deleteAllCommand == null)
                {
                    _deleteAllCommand = _driveIndexDatabase.CreateCommand();
                    _deleteAllCommand.CommandText = @"DELETE FROM tagindex WHERE fileid=$fileid";
                    _dallparam1 = _deleteAllCommand.CreateParameter();
                    _dallparam1.ParameterName = "$fileid";
                    _deleteAllCommand.Parameters.Add(_dallparam1);
                }

                _dallparam1.Value = FileId;
                _deleteAllCommand.ExecuteNonQuery();
            }
        }
    }
}