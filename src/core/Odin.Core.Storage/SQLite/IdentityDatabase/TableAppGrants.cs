using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        ~TableAppGrants()
        {
        }

        public int Insert(AppGrantsRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Upsert(AppGrantsRecord item)
        {
            item.identityId = _db._identityId;

            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public List<AppGrantsRecord> GetByOdinHashId(Guid odinHashId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByOdinHashId(conn, _db._identityId, odinHashId);
            }
        }

        public void DeleteByIdentity(Guid odinHashId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                var r = GetByOdinHashId(conn, _db._identityId, odinHashId);

                if (r == null)
                    return;

                conn.CreateCommitUnitOfWork(() =>
                {
                    for (int i = 0; i < r.Count; i++)
                    {
                        Delete(conn, _db._identityId, odinHashId, r[i].appId, r[i].circleId);
                    }
                });
            }
        }
    }
}
