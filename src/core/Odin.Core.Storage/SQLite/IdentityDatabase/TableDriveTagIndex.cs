using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveTagIndex : TableDriveTagIndexCRUD
    {
        private readonly IdentityDatabase _db;

        public TableDriveTagIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }

        ~TableDriveTagIndex()
        {
        }

        public DriveTagIndexRecord Get(Guid driveId, Guid fileId, Guid tagId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId, tagId);
            }
        }

        public List<Guid> Get(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, _db._identityId, driveId, fileId);
            }
        }

        public int Insert(DriveTagIndexRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int DeleteAllRows(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.DeleteAllRows(conn, _db._identityId, driveId, fileId);
            }
        }

        public void InsertRows(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveTagIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    base.Insert(conn, item);
                }
            });
            }
        }


        internal void InsertRows(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            conn.CreateCommitUnitOfWork(() =>
            {
                var item = new DriveTagIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    base.Insert(conn, item);
                }
            });
        }

        public void DeleteRow(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < tagIdList.Count; i++)
                    {
                        base.Delete(conn, _db._identityId, driveId, fileId, tagIdList[i]);
                    }
                });
            }
        }
    }
}