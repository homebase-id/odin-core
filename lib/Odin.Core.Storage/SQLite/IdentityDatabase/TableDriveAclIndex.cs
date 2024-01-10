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


        public void InsertRows(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            using (_database.CreateCommitUnitOfWork())
            {
                var item = new DriveAclIndexRecord() { driveId = driveId, fileId = fileId };
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    Delete(driveId, fileId, accessControlList[i]);
                }
            }
        }
    }
}