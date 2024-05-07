﻿using System;
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


        public void InsertRows(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveAclIndexRecord() { driveId = driveId, fileId = fileId };
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    Insert(conn, item);
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
                    Delete(conn, driveId, fileId, accessControlList[i]);
                }
            });
        }
    }
}