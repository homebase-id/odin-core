using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveTagIndex : TableDriveTagIndexCRUD
    {
        public TableDriveTagIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableDriveTagIndex()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }


        public void InsertRows(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                var item = new DriveTagIndexRecord() { driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < tagIdList.Count; i++)
                {
                    Delete(driveId, fileId, tagIdList[i]);
                }
            }
        }
    }
}