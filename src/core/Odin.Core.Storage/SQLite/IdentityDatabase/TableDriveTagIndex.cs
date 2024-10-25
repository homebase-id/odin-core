using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveTagIndex : TableDriveTagIndexCRUD
    {
        private readonly IdentityDatabase _db;

        public TableDriveTagIndex(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<DriveTagIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid tagId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId, tagId);
        }

        public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId);
        }

        public async Task<int> InsertAsync(DriveTagIndexRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAllRowsAsync(conn, _db._identityId, driveId, fileId);
        }

        public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    var item = new DriveTagIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                    for (int i = 0; i < tagIdList.Count; i++)
                    {
                        item.tagId = tagIdList[i];
                        await base.InsertAsync(conn, item);
                    }
                });
            }
        }

        internal async Task InsertRowsAsync(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            await conn.CreateCommitUnitOfWorkAsync(async () =>
            {
                var item = new DriveTagIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                for (int i = 0; i < tagIdList.Count; i++)
                {
                    item.tagId = tagIdList[i];
                    await base.InsertAsync(conn, item);
                }
            });
        }

        public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> tagIdList)
        {
            if (tagIdList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < tagIdList.Count; i++)
                    {
                        await base.DeleteAsync(conn, _db._identityId, driveId, fileId, tagIdList[i]);
                    }
                });
            }
        }
    }
}