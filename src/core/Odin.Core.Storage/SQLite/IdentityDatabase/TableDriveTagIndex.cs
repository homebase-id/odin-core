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

        public void InsertRows(DatabaseBase.DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (conn.CreateCommitUnitOfWork())
            {
                var item = new DriveTagIndexRecord() { driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    Insert(conn, item);
                }
            }
        }

        public void DeleteRow(DatabaseBase.DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (this._database != conn.db)
                throw new Exception("Database mixup");

            if (tagIdList == null)
                return;

            using (conn.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < tagIdList.Count; i++)
                {
                    Delete(conn, driveId, fileId, tagIdList[i]);
                }
            }
        }
    }
}