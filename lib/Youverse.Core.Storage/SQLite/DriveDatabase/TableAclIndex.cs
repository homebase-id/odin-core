using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class TableAclIndex : TableAclIndexCRUD
    {
        private SQLiteCommand _deleteAllCommand = null;
        private SQLiteParameter _dallparam1 = null;
        private Object _deleteAllLock = new Object();


        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private  Object _selectLock = new Object();

        public TableAclIndex(DriveDatabase db) : base(db)
        {
        }

        ~TableAclIndex()
        {
        }


        public override void Dispose()
        {
            _selectCommand?.Dispose();
            _selectCommand = null;

            _deleteAllCommand?.Dispose();
            _deleteAllCommand = null;

            base.Dispose();
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
                    _selectCommand.CommandText = @"SELECT aclmemberid FROM aclindex WHERE fileid=$fileid";
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

            // Since we are writing multiple rows we do a logic unit here
            using (_database.CreateCommitUnitOfWork())
            {
                var item = new AclIndexItem() { fileId = FileId };
                for (int i = 0; i < AccessControlList.Count; i++)
                {
                    item.aclMemberId = AccessControlList[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(Guid FileId, List<Guid> AccessControlList)
        {
            if (AccessControlList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < AccessControlList.Count; i++)
                {
                    Delete(FileId, AccessControlList[i]);
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