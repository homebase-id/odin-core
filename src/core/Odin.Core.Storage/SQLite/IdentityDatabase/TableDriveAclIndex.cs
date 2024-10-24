using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

        public async Task<DriveAclIndexRecord> GetAsync(Guid driveId, Guid fileId, Guid aclMemberId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId, aclMemberId);
        }

        public async Task<List<Guid>> GetAsync(Guid driveId, Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, driveId, fileId);
        }

        public async Task<int> DeleteAllRowsAsync(Guid driveId, Guid fileId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAllRowsAsync(conn, _db._identityId, driveId, fileId);
        }

        public async Task<int> InsertAsync(DriveAclIndexRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        internal async Task InsertRowsAsync(DatabaseConnection conn, Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            // Since we are writing multiple rows we do a logic unit here
            await conn.CreateCommitUnitOfWorkAsync(async () =>
            {
                var item = new DriveAclIndexRecord { identityId = _db._identityId, driveId = driveId, fileId = fileId };
                for (int i = 0; i < accessControlList.Count; i++)
                {
                    item.aclMemberId = accessControlList[i];
                    await base.InsertAsync(conn, item);
                }
            });
        }


        public async Task InsertRowsAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                // Since we are writing multiple rows we do a logic unit here
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    var item = new DriveAclIndexRecord() { identityId = _db._identityId, driveId = driveId, fileId = fileId };

                    for (int i = 0; i < accessControlList.Count; i++)
                    {
                        item.aclMemberId = accessControlList[i];
                        await base.InsertAsync(conn, item);
                    }
                });
            }
        }

        public async Task DeleteRowAsync(Guid driveId, Guid fileId, List<Guid> accessControlList)
        {
            if (accessControlList == null)
                return;

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < accessControlList.Count; i++)
                    {
                        await base.DeleteAsync(conn, _db._identityId, driveId, fileId, accessControlList[i]);
                    }
                });
            }
        }
    }
}