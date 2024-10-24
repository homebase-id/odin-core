using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyValue : TableKeyValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyValue(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public new async Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            return await base.GetCountDirtyAsync(conn);
        }

        public async Task<KeyValueRecord> GetAsync(byte[] key)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, key);
        }
        public async Task<int> InsertAsync(KeyValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }
        public async Task<int> DeleteAsync(byte[] key)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, key);
        }

        public async Task<int> UpsertAsync(KeyValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }

        public async Task<int> UpsertManyAsync(List<KeyValueRecord> items)
        {
            int affectedRows = 0;

            using (var conn = _db.CreateDisposableConnection())
            {
                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    foreach (var item in items)
                    {
                        item.identityId = _db._identityId;
                        affectedRows += await base.UpsertAsync(conn, item);
                    }
                });
            }
            return affectedRows;
        }

        public async Task<int> UpdateAsync(KeyValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpdateAsync(conn, item);
        }
    }
}