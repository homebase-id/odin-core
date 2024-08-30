using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Time;
using Org.BouncyCastle.Crypto.Engines;

namespace Odin.Core.Storage.SQLite.IdentityDatabase
{
    public class TableDriveMainIndex : TableDriveMainIndexCRUD
    {
        private readonly IdentityDatabase _db;

        public TableDriveMainIndex(IdentityDatabase db, CacheHelper cache) : base(db, cache)
        {
            _db = db;
        }


        ~TableDriveMainIndex()
        {
        }


        public override void Dispose()
        {
            base.Dispose();
            GC.SuppressFinalize(this);
        }

        public DriveMainIndexRecord GetByUniqueId(Guid driveId, Guid? uniqueId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByUniqueId(conn, _db._identityId, driveId, uniqueId);
            }
        }

        public DriveMainIndexRecord GetByGlobalTransitId(Guid driveId, Guid? globalTransitId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.GetByGlobalTransitId(conn, _db._identityId, driveId, globalTransitId);
            }
        }

        public DriveMainIndexRecord Get(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Get(conn, ((IdentityDatabase)conn.db)._identityId, driveId, fileId);
            }
        }

        public int Insert(DriveMainIndexRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Insert(conn, item);
            }
        }

        public int Delete(Guid driveId, Guid fileId)
        {
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Delete(conn, _db._identityId, driveId, fileId);
            }
        }

        public int Update(DriveMainIndexRecord item)
        {
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Update(conn, item);
            }
        }

        public int Upsert(DriveMainIndexRecord item)
        { 
            item.identityId = _db._identityId;
            using (var conn = _db.CreateDisposableConnection())
            {
                return base.Upsert(conn, item);
            }
        }

        public int UpdateReactionSummary(Guid driveId, Guid fileId, string reactionSummary)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText =
                    $"UPDATE driveMainIndex SET modified=@modified,hdrReactionSummary=@hdrReactionSummary WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

                var _sparam1 = _updateCommand.CreateParameter();
                var _sparam2 = _updateCommand.CreateParameter();
                var _sparam3 = _updateCommand.CreateParameter();
                var _sparam4 = _updateCommand.CreateParameter();
                var _sparam5 = _updateCommand.CreateParameter();

                _sparam1.ParameterName = "@identityId";
                _sparam2.ParameterName = "@driveId";
                _sparam3.ParameterName = "@fileId";
                _sparam4.ParameterName = "@hdrReactionSummary";
                _sparam5.ParameterName = "@modified";

                _updateCommand.Parameters.Add(_sparam1);
                _updateCommand.Parameters.Add(_sparam2);
                _updateCommand.Parameters.Add(_sparam3);
                _updateCommand.Parameters.Add(_sparam4);
                _updateCommand.Parameters.Add(_sparam5);

                _sparam1.Value = _db._identityId.ToByteArray();
                _sparam2.Value = driveId.ToByteArray();
                _sparam3.Value = fileId.ToByteArray();
                _sparam4.Value = reactionSummary;
                _sparam5.Value = UnixTimeUtcUnique.Now().uniqueTime;

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_updateCommand);
                }
            }
        }

        public int UpdateTransferStatus(Guid driveId, Guid fileId, string transferStatus)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText =
                    $"UPDATE driveMainIndex SET modified=@modified,hdrTransferStatus=@hdrTransferStatus WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

                var _sparam1 = _updateCommand.CreateParameter();
                var _sparam2 = _updateCommand.CreateParameter();
                var _sparam3 = _updateCommand.CreateParameter();
                var _sparam4 = _updateCommand.CreateParameter();
                var _sparam5 = _updateCommand.CreateParameter();

                _sparam1.ParameterName = "@identityId";
                _sparam2.ParameterName = "@driveId";
                _sparam3.ParameterName = "@fileId";
                _sparam4.ParameterName = "@hdrTransferStatus";
                _sparam5.ParameterName = "@modified";

                _updateCommand.Parameters.Add(_sparam1);
                _updateCommand.Parameters.Add(_sparam2);
                _updateCommand.Parameters.Add(_sparam3);
                _updateCommand.Parameters.Add(_sparam4);
                _updateCommand.Parameters.Add(_sparam5);

                _sparam1.Value = _db._identityId.ToByteArray();
                _sparam2.Value = driveId.ToByteArray();
                _sparam3.Value = fileId.ToByteArray();
                _sparam4.Value = transferStatus;
                _sparam5.Value = UnixTimeUtcUnique.Now().uniqueTime;

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_updateCommand);
                }
            }
        }

        public (Int64, Int64) GetDriveSizeDirty(Guid driveId)
        {
            using (var _sizeCommand = _database.CreateCommand())
            {
                _sizeCommand.CommandText =
                    $"PRAGMA read_uncommitted = 1; SELECT count(*), sum(byteCount) FROM drivemainindex WHERE identityId=$identityId AND driveid=$driveId; PRAGMA read_uncommitted = 0;";

                var _sparam1 = _sizeCommand.CreateParameter();
                _sparam1.ParameterName = "$driveId";
                _sizeCommand.Parameters.Add(_sparam1);

                var _sparam2 = _sizeCommand.CreateParameter();
                _sparam2.ParameterName = "$identityId";
                _sizeCommand.Parameters.Add(_sparam2);

                _sparam1.Value = driveId.ToByteArray();
                _sparam2.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    using (SqliteDataReader rdr = conn.ExecuteReader(_sizeCommand, System.Data.CommandBehavior.Default))
                    {
                        if (rdr.Read())
                        {
                            long count = rdr.IsDBNull(0) ? 0 : rdr.GetInt64(0);
                            long size = rdr.IsDBNull(1) ? 0 : rdr.GetInt64(1);
                            return (count, size);
                        }
                    }
                }
            }

            return (-1, -1);
        }


        /// <summary>
        /// For testing only. Updates the updatedTimestamp for the supplied item.
        /// </summary>
        /// <param name="fileId">Item to touch</param>
        public int TestTouch(Guid driveId, Guid fileId)
        {
            using (var _touchCommand = _database.CreateCommand())
            {
                _touchCommand.CommandText =
                    $"UPDATE drivemainindex SET modified=$modified WHERE identityId = $identityId AND driveId = $driveId AND fileid = $fileid;";

                var _tparam1 = _touchCommand.CreateParameter();
                var _tparam2 = _touchCommand.CreateParameter();
                var _tparam3 = _touchCommand.CreateParameter();
                var _tparam4 = _touchCommand.CreateParameter();

                _tparam1.ParameterName = "$fileid";
                _tparam2.ParameterName = "$modified";
                _tparam3.ParameterName = "$driveId";
                _tparam4.ParameterName = "$identityId";

                _touchCommand.Parameters.Add(_tparam1);
                _touchCommand.Parameters.Add(_tparam2);
                _touchCommand.Parameters.Add(_tparam3);
                _touchCommand.Parameters.Add(_tparam4);

                _tparam1.Value = fileId.ToByteArray();
                _tparam2.Value = UnixTimeUtcUniqueGenerator.Generator().uniqueTime;
                _tparam3.Value = driveId.ToByteArray();
                _tparam4.Value = _db._identityId.ToByteArray();

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_touchCommand);
                }
            }
        }
   }
}