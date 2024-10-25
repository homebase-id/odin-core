using System;
using System.Collections.Generic;

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
            void DoDelete(DatabaseConnection conn)
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

            using (var conn = _db.CreateDisposableConnection())
            {
                DoDelete(conn);
            }
        }
    }
}