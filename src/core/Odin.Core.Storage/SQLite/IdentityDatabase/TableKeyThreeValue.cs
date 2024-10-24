using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableKeyThreeValue : TableKeyThreeValueCRUD
    {
        private readonly IdentityDatabase _db;

        public TableKeyThreeValue(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<KeyThreeValueRecord> GetAsync(byte[] key1)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetAsync(conn, _db._identityId, key1);
        }

        public async Task<List<byte[]>> GetByKeyTwoAsync(byte[] key2)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetByKeyTwoAsync(conn, _db._identityId, key2);
        }
        
        public async Task<List<byte[]>> GetByKeyThreeAsync(byte[] key3)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetByKeyThreeAsync(conn, _db._identityId, key3);
        }
        
        public async Task<List<KeyThreeValueRecord>> GetByKeyTwoThreeAsync(byte[] key2, byte[] key3)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetByKeyTwoThreeAsync(conn, _db._identityId, key2, key3);
        }

        public async Task<int> UpsertAsync(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }

        public async Task<int> InsertAsync(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> DeleteAsync(byte[] key1)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.DeleteAsync(conn, _db._identityId, key1);
        }

        public async Task<int> UpdateAsync(KeyThreeValueRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpdateAsync(conn, item);
        }

    }
}