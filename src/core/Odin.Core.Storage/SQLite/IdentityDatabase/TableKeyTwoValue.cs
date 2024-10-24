using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyTwoValue : TableKeyTwoValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyTwoValue(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(byte[] key2)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetByKeyTwoAsync(conn, _db._identityId, key2);
        }

        public async Task<KeyTwoValueRecord> GetAsync(byte[] key1)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, key1);
        }

        public async Task<int> DeleteAsync(byte[] key1)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, key1);
        }

        public async Task<int> InsertAsync(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> UpsertAsync(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }

        public async Task<int> UpdateAsync(KeyTwoValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpdateAsync(conn, item);
        }
    }
}
