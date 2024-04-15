using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveAclIndex : TableDriveAclIndexCRUD
    {
        public TableDriveAclIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableDriveAclIndex()
        {
        }


        public override void Dispose()
        {
            base.Dispose();
        }


        public void InsertRows(DatabaseBase.DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            using (conn.CreateCommitUnitOfWork())
            {
                var item = new DriveAclIndexRecord() { driveId = driveId, fileId = fileId };
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    Insert(conn, item);
                }
            }
        }

        public void DeleteRow(DatabaseBase.DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (conn.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    Delete(conn, driveId, fileId, accessControlList[i]);
                }
            }
        }
    }
}