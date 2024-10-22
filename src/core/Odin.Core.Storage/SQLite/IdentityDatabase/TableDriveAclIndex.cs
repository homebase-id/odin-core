using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IdentityDatabase")]

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveAclIndex : TableDriveAclIndexCRUD
    {
        private readonly IdentityDatabase _db;

        public TableDriveAclIndex(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        ~TableDriveAclIndex()
        {
        }

        public DriveAclIndexRecord Get(Guid driveId, Guid fileId, Guid aclMemberId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId, aclMemberId);
            }
        }

        public List<Guid> Get(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId);
            }
        }

        public int DeleteAllRows(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.DeleteAllRows(conn, _db._identityId, driveId, fileId);
            }
        }

        public int Insert(DriveAclIndexRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        internal void InsertRows(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveAclIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    base.Insert(conn, item);
                }
            });
        }


        public void InsertRows(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                // Since we are writing multiple rows we do a logic unit here
                conn.CreateCommitUnitOfWork(() =>
                {
                    var item = new DriveAclIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                    for (int i = 0; i < accessControlList.Count; i++)
                    {
                        item.aclMemberId = accessControlList[i];
                        base.Insert(conn, item);
                    }
                });
            }
        }

        public void DeleteRow(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < accessControlList.Count; i++)
                    {
                        base.Delete(conn, _db._identityId, driveId, fileId, accessControlList[i]);
                    }
                });
            }
        }
    }
}