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

        public new DriveAclIndexRecord Get(DatabaseConnection conn, Guid driveId, Guid fileId, Guid aclMemberId)
        {
            return base.Get(conn, ((IdentityDatabase) conn.db)._identityId, driveId, fileId, aclMemberId);
        }

        public List<Guid> Get(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId);
        }

        public int DeleteAllRows(DatabaseConnection conn, Guid driveId, Guid fileId)
        {
            return base.DeleteAllRows(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId);
        }

        public new int Insert(DatabaseConnection conn, DriveAclIndexRecord item)
        {
            item.identityId = ((IdentityDatabase)conn.db)._identityId;
            return base.Insert(conn, item);
        }

        public void InsertRows(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveAclIndexRecord() { identityId = ((IdentityDatabase)conn.db)._identityId, driveId = driveId, fileId = fileId };

                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    base.Insert(conn, item);
                }
            });
        }

        public void DeleteRow(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            conn.CreateCommitUnitOfWork(() =>
            {
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    base.Delete(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId, accessControlList[i]);
                }
            });
        }
    }
}