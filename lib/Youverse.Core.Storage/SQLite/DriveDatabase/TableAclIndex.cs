using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite
{
    public class TableAclIndex : TableBase
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
        private  Object _selectLock = new Object();

        public TableAclIndex(DriveIndexDatabase db) : base(db)
        {
        }

        ~TableAclIndex()
        {
        }


        public override void Dispose()
        {
            _insertCommand?.Dispose();
            _insertCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;

            _deleteCommand?.Dispose();
            _deleteCommand = null;

            _deleteAllCommand?.Dispose();
            _deleteAllCommand = null;
        }


        public override void EnsureTableExists(bool dropExisting = false)
        {
            using (var cmd = _database.CreateCommand())
            {
                if(dropExisting)
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS aclindex;";
                    cmd.ExecuteNonQuery();
                }
                
                cmd.CommandText = @"CREATE TABLE if not exists aclindex(fileid BLOB NOT NULL, aclmember BLOB NOT NULL, UNIQUE(fileid, aclmember));"
                                  + "CREATE INDEX if not exists AclIdx ON aclindex(aclmember);";
                cmd.ExecuteNonQuery();
            }
        }

        // I cannot decide if no result should return null or an empty list...
        public List<Guid> Get(Guid fileId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
                    _selectCommand.CommandText = @"SELECT aclmember FROM aclindex WHERE fileid=$fileid";
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


        public void InsertRows(Guid FileId, List<Guid> AccessControlList)
        {
            if (AccessControlList == null)
                return;

            lock (_insertLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_insertCommand == null)
                {
                    _insertCommand = _database.CreateCommand();
                    _insertCommand.CommandText = @"INSERT INTO aclindex(fileid, aclmember) VALUES($fileid, $aclmember)";
                    _iparam1 = _insertCommand.CreateParameter();
                    _iparam1.ParameterName = "$fileid";
                    _iparam2 = _insertCommand.CreateParameter();
                    _iparam2.ParameterName = "$aclmember";
                    _insertCommand.Parameters.Add(_iparam1);
                    _insertCommand.Parameters.Add(_iparam2);
                }

                _database.BeginTransaction();

                // Since we are writing multiple rows we do a logic unit here
                using (_database.CreateLogicCommitUnit())
                {
                    for (int i = 0; i < AccessControlList.Count; i++)
                    {
                        _iparam1.Value = FileId;
                        _iparam2.Value = AccessControlList[i];
                        _insertCommand.ExecuteNonQuery();
                    }
                }
            }
        }

        public void DeleteRow(Guid FileId, List<Guid> AccessControlList)
        {
            if (AccessControlList == null)
                return;

            lock (_deleteLock)
            {
                // Make sure we only prep once - I wish I had been able to use local static vars
                // rather then class members
                if (_deleteCommand == null)
                {
                    _deleteCommand = _database.CreateCommand();
                    _deleteCommand.CommandText = @"DELETE FROM aclindex WHERE fileid=$fileid AND aclmember=$aclmember";
                    _dparam1 = _deleteCommand.CreateParameter();
                    _dparam1.ParameterName = "$fileid";
                    _dparam2 = _deleteCommand.CreateParameter();
                    _dparam2.ParameterName = "$aclmember";
                    _deleteCommand.Parameters.Add(_dparam1);
                    _deleteCommand.Parameters.Add(_dparam2);
                }

                _database.BeginTransaction();

                // Since we are deleting multiple rows we do a logic unit here
                using (_database.CreateLogicCommitUnit())
                {
                    _dparam1.Value = FileId;
                    for (int i = 0; i < AccessControlList.Count; i++)
                    {
                        _dparam2.Value = AccessControlList[i];
                        _deleteCommand.ExecuteNonQuery();
                    }
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
                    _deleteAllCommand = _database.CreateCommand();
                    _deleteAllCommand.CommandText = @"DELETE FROM aclindex WHERE fileid=$fileid";
                    _dallparam1 = _deleteAllCommand.CreateParameter();
                    _dallparam1.ParameterName = "$fileid";
                    _deleteAllCommand.Parameters.Add(_dallparam1);
                }

                _dallparam1.Value = FileId;

                _database.BeginTransaction();
                _deleteAllCommand.ExecuteNonQuery();
            }
        }
    }
}