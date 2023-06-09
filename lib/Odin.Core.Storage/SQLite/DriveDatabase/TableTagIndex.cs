using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.DriveDatabase
{
    public class TableTagIndex : TableTagIndexCRUD
    {
        public TableTagIndex(DriveDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableTagIndex()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }


        public void InsertRows(Guid FileId, List<Guid> TagIdList)
        {
            if (TagIdList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                var item = new TagIndexRecord() { fileId = FileId };

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
    }
}