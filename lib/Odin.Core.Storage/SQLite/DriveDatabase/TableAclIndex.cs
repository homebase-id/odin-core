﻿using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.SQLite.DriveDatabase
{
    public class TableAclIndex : TableAclIndexCRUD
    {
        public TableAclIndex(xDriveDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableAclIndex()
        {
        }


        public override void Dispose()
        {
            base.Dispose();
        }


        public void InsertRows(Guid FileId, List<Guid> AccessControlList)
        {
            if (AccessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            using (_database.CreateCommitUnitOfWork())
            {
                var item = new AclIndexRecord() { fileId = FileId };
                for (int i = 0; i < AccessControlList.Count; i++)
                {
                    item.aclMemberId = AccessControlList[i];
                    Insert(item);
                }
            }
        }

        public void DeleteRow(Guid FileId, List<Guid> AccessControlList)
        {
            if (AccessControlList == null)
                return;

            using (_database.CreateCommitUnitOfWork())
            {
                for (int i = 0; i < AccessControlList.Count; i++)
                {
                    Delete(FileId, AccessControlList[i]);
                }
            }
        }
    }
}