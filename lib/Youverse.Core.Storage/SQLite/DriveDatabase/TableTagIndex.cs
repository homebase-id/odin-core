using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class TableTagIndex : TableTagIndexCRUD
    {
        private SqliteCommand _deleteAllCommand = null;
        private SqliteParameter _dallparam1 = null;
        private Object _deleteAllLock = new Object();

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

            base.Dispose();
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
                _deleteAllCommand.ExecuteNonQuery(_database);
            }
        }
    }
}