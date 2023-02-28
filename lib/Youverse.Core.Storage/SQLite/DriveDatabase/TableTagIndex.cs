using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Youverse.Core.Storage.SQLite.DriveDatabase
{
    public class TableTagIndex : TableTagIndexCRUD
    {
        private SQLiteCommand _deleteAllCommand = null;
        private SQLiteParameter _dallparam1 = null;
        private Object _deleteAllLock = new Object();

        private SQLiteCommand _selectCommand = null;
        private SQLiteParameter _sparam1 = null;
        private Object _selectLock = new Object();

        public TableTagIndex(DriveDatabase db) : base(db)
        {
        }

        ~TableTagIndex()
        {
        }

        public override void Dispose()
        {
            _deleteAllCommand?.Dispose();
            _deleteAllCommand = null;

            _selectCommand?.Dispose();
            _selectCommand = null;

            base.Dispose();
        }

        // Hm is it better to return an empty List<Guid> rather than null for an empty set?
        public List<Guid> Get(Guid fileId)
        {
            lock (_selectLock)
            {
                // Make sure we only prep once 
                if (_selectCommand == null)
                {
                    _selectCommand = _database.CreateCommand();
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

            using (_database.CreateCommitUnitOfWork())
            {
                var item = new TagIndexItem() { fileId = FileId };

                for (int i = 0; i < TagIdList.Count; i++)
                {
                    item.tagId = TagIdList[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(Guid FileId, List<Guid> TagIdList)
        {
            if (TagIdList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < TagIdList.Count; i++)
                {
                    Delete(FileId, TagIdList[i]);
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
                    _deleteAllCommand.CommandText = @"DELETE FROM tagindex WHERE fileid=$fileid";
                    _dallparam1 = _deleteAllCommand.CreateParameter();
                    _dallparam1.ParameterName = "$fileid";
                    _deleteAllCommand.Parameters.Add(_dallparam1);
                }

                _database.BeginTransaction();
                _dallparam1.Value = FileId;
                _deleteAllCommand.ExecuteNonQuery();
            }
        }
    }
}