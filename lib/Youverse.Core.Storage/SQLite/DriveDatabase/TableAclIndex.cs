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


        public TableAclIndex(DriveDatabase db) : base(db)
        {
        }

        ~TableAclIndex()
        {
        }


        public override void Dispose()
        {
            _deleteAllCommand?.Dispose();
            _deleteAllCommand = null;

            base.Dispose();
        }


        // I cannot decide if no result should return null or an empty list...
        public new virtual List<Guid> Get(Guid fileId)
        {
            var r = base.Get(fileId);

            // The services layer does not handle null for now
            if (r == null)
                r = new List<Guid>();

            return r;
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