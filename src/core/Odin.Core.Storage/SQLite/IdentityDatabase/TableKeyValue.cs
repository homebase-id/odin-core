using System;
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

        public new Task<int> GetCountDirtyAsync(DatabaseConnection conn)
        {
            return base.GetCountDirtyAsync(conn);
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

        public async Task<int> UpdateAsync(KeyValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpdateAsync(conn, item);
        }
    }
}