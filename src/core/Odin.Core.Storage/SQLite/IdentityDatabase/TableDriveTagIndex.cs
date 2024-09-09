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

        public new DriveTagIndexRecord Get(DatabaseConnection conn, Guid driveId, Guid fileId, Guid tagId)
        {
            return base.Get(conn, ((IdentityDatabase) conn.db)._identityId, driveId, fileId, tagId);
        }

        public List<Guid> Get(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId);
        }

        public new int Insert(DatabaseConnection conn, DriveTagIndexRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public int DeleteAllRows(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            return base.DeleteAllRows(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId);
        }

        public void InsertRows(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveTagIndexRecord() { identityId = ((IdentityDatabase)conn.db)._identityId,  driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    base.Insert(conn, item);
                }
            });
        }

        public void DeleteRow(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (this._database != conn.db)
                throw new Exception("Database mixup");

            if (tagIdList == null)
                return;

            conn.CreateCommitUnitOfWork(() =>
            {
                for (int i = 0; i < tagIdList.Count; i++)
                {
                    base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId, tagIdList[i]);
                }
            });
        }
    }
}