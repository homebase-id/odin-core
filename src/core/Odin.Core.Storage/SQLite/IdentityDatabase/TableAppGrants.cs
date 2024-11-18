using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using static Odin.Core.Storage.SQLite.DatabaseBase;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableAppGrants : TableAppGrantsCRUD
    {
        private readonly IdentityDatabase _db;

        public TableAppGrants(IdentityDatabase db, CacheHelper cache) : base(cache)
        {
            _db = db;
        }

        public async Task<int> InsertAsync(AppGrantsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.InsertAsync(conn, item);
        }

        public async Task<int> UpsertAsync(AppGrantsRecord item)
        {
            item.identityId = _db._identityId;
            using var conn = _db.CreateDisposableConnection();
            return await base.UpsertAsync(conn, item);
        }

        public async Task<List<AppGrantsRecord>> GetByOdinHashIdAsync(Guid odinHashId)
        {
            using var conn = _db.CreateDisposableConnection();
            return await base.GetByOdinHashIdAsync(conn, _db._identityId, odinHashId);
        }

        public async Task DeleteByIdentityAsync(Guid odinHashId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                var r = await GetByOdinHashIdAsync(conn, _db._identityId, odinHashId);

                if (r == null)
                    return;

                await conn.CreateCommitUnitOfWorkAsync(async () =>
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        await DeleteAsync(conn, _db._identityId, odinHashId, r[i].appId, r[i].circleId);
                    }
                });
            }
        }
    }
}
