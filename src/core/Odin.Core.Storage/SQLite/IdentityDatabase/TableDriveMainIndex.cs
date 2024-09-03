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

        internal int Insert(DriveMainIndexRecord item)
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

        public int UpdateTransferHistory(Guid driveId, Guid fileId, string transferHistory)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText =
                    $"UPDATE driveMainIndex SET modified=@modified,hdrTransferHistory=@hdrTransferHistory WHERE identityId=@identityId AND driveid=@driveId AND fileId=@fileId;";

                var _sparam1 = _updateCommand.CreateParameter();
                var _sparam2 = _updateCommand.CreateParameter();
                var _sparam3 = _updateCommand.CreateParameter();
                var _sparam4 = _updateCommand.CreateParameter();
                var _sparam5 = _updateCommand.CreateParameter();

                _sparam1.ParameterName = "@identityId";
                _sparam2.ParameterName = "@driveId";
                _sparam3.ParameterName = "@fileId";
                _sparam4.ParameterName = "@hdrTransferHistory";
                _sparam5.ParameterName = "@modified";

                _updateCommand.Parameters.Add(_sparam1);
                _updateCommand.Parameters.Add(_sparam2);
                _updateCommand.Parameters.Add(_sparam3);
                _updateCommand.Parameters.Add(_sparam4);
                _updateCommand.Parameters.Add(_sparam5);

                _sparam1.Value = _db._identityId.ToByteArray();
                _sparam2.Value = driveId.ToByteArray();
                _sparam3.Value = fileId.ToByteArray();
                _sparam4.Value = transferHistory;
                _sparam5.Value = UnixTimeUtcUnique.Now().uniqueTime;

                using (var conn = _db.CreateDisposableConnection())
                {
                    return conn.ExecuteNonQuery(_updateCommand);
                }
            }
        }

        public int UpdateAllButReactionsAndTransfer(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            using (var _updateCommand = _database.CreateCommand())
            {
                _updateCommand.CommandText = "UPDATE driveMainIndex " +
                                             "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrServerData = @hdrServerData,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = @modified " +
                                             "WHERE (identityId = @identityId AND driveId = @driveId AND fileId = @fileId)";
                var _updateParam1 = _updateCommand.CreateParameter();
                _updateParam1.ParameterName = "@identityId";
                _updateCommand.Parameters.Add(_updateParam1);
                var _updateParam2 = _updateCommand.CreateParameter();
                _updateParam2.ParameterName = "@driveId";
                _updateCommand.Parameters.Add(_updateParam2);
                var _updateParam3 = _updateCommand.CreateParameter();
                _updateParam3.ParameterName = "@fileId";
                _updateCommand.Parameters.Add(_updateParam3);
                var _updateParam4 = _updateCommand.CreateParameter();
                _updateParam4.ParameterName = "@globalTransitId";
                _updateCommand.Parameters.Add(_updateParam4);
                var _updateParam5 = _updateCommand.CreateParameter();
                _updateParam5.ParameterName = "@fileState";
                _updateCommand.Parameters.Add(_updateParam5);
                var _updateParam6 = _updateCommand.CreateParameter();
                _updateParam6.ParameterName = "@requiredSecurityGroup";
                _updateCommand.Parameters.Add(_updateParam6);
                var _updateParam7 = _updateCommand.CreateParameter();
                _updateParam7.ParameterName = "@fileSystemType";
                _updateCommand.Parameters.Add(_updateParam7);
                var _updateParam8 = _updateCommand.CreateParameter();
                _updateParam8.ParameterName = "@userDate";
                _updateCommand.Parameters.Add(_updateParam8);
                var _updateParam9 = _updateCommand.CreateParameter();
                _updateParam9.ParameterName = "@fileType";
                _updateCommand.Parameters.Add(_updateParam9);
                var _updateParam10 = _updateCommand.CreateParameter();
                _updateParam10.ParameterName = "@dataType";
                _updateCommand.Parameters.Add(_updateParam10);
                var _updateParam11 = _updateCommand.CreateParameter();
                _updateParam11.ParameterName = "@archivalStatus";
                _updateCommand.Parameters.Add(_updateParam11);
                var _updateParam12 = _updateCommand.CreateParameter();
                _updateParam12.ParameterName = "@historyStatus";
                _updateCommand.Parameters.Add(_updateParam12);
                var _updateParam13 = _updateCommand.CreateParameter();
                _updateParam13.ParameterName = "@senderId";
                _updateCommand.Parameters.Add(_updateParam13);
                var _updateParam14 = _updateCommand.CreateParameter();
                _updateParam14.ParameterName = "@groupId";
                _updateCommand.Parameters.Add(_updateParam14);
                var _updateParam15 = _updateCommand.CreateParameter();
                _updateParam15.ParameterName = "@uniqueId";
                _updateCommand.Parameters.Add(_updateParam15);
                var _updateParam16 = _updateCommand.CreateParameter();
                _updateParam16.ParameterName = "@byteCount";
                _updateCommand.Parameters.Add(_updateParam16);
                var _updateParam17 = _updateCommand.CreateParameter();
                _updateParam17.ParameterName = "@hdrEncryptedKeyHeader";
                _updateCommand.Parameters.Add(_updateParam17);
                var _updateParam18 = _updateCommand.CreateParameter();
                _updateParam18.ParameterName = "@hdrVersionTag";
                _updateCommand.Parameters.Add(_updateParam18);
                var _updateParam19 = _updateCommand.CreateParameter();
                _updateParam19.ParameterName = "@hdrAppData";
                _updateCommand.Parameters.Add(_updateParam19);
                var _updateParam20 = _updateCommand.CreateParameter();
                _updateParam20.ParameterName = "@hdrReactionSummary";
                _updateCommand.Parameters.Add(_updateParam20);
                var _updateParam21 = _updateCommand.CreateParameter();
                _updateParam21.ParameterName = "@hdrServerData";
                _updateCommand.Parameters.Add(_updateParam21);
                var _updateParam22 = _updateCommand.CreateParameter();
                _updateParam22.ParameterName = "@hdrTransferHistory";
                _updateCommand.Parameters.Add(_updateParam22);
                var _updateParam23 = _updateCommand.CreateParameter();
                _updateParam23.ParameterName = "@hdrFileMetaData";
                _updateCommand.Parameters.Add(_updateParam23);
                var _updateParam24 = _updateCommand.CreateParameter();
                _updateParam24.ParameterName = "@hdrTmpDriveAlias";
                _updateCommand.Parameters.Add(_updateParam24);
                var _updateParam25 = _updateCommand.CreateParameter();
                _updateParam25.ParameterName = "@hdrTmpDriveType";
                _updateCommand.Parameters.Add(_updateParam25);
                var _updateParam26 = _updateCommand.CreateParameter();
                _updateParam26.ParameterName = "@created";
                _updateCommand.Parameters.Add(_updateParam26);
                var _updateParam27 = _updateCommand.CreateParameter();
                _updateParam27.ParameterName = "@modified";
                _updateCommand.Parameters.Add(_updateParam27);
                var now = UnixTimeUtcUnique.Now();
                _updateParam1.Value = item.identityId.ToByteArray();
                _updateParam2.Value = item.driveId.ToByteArray();
                _updateParam3.Value = item.fileId.ToByteArray();
                _updateParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam5.Value = item.fileState;
                _updateParam6.Value = item.requiredSecurityGroup;
                _updateParam7.Value = item.fileSystemType;
                _updateParam8.Value = item.userDate.milliseconds;
                _updateParam9.Value = item.fileType;
                _updateParam10.Value = item.dataType;
                _updateParam11.Value = item.archivalStatus;
                _updateParam12.Value = item.historyStatus;
                _updateParam13.Value = item.senderId ?? (object)DBNull.Value;
                _updateParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _updateParam16.Value = item.byteCount;
                _updateParam17.Value = item.hdrEncryptedKeyHeader;
                _updateParam18.Value = item.hdrVersionTag.ToByteArray();
                _updateParam19.Value = item.hdrAppData;
                _updateParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value; // Not used
                _updateParam21.Value = item.hdrServerData;
                _updateParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value; // Not used
                _updateParam23.Value = item.hdrFileMetaData;
                _updateParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
                _updateParam25.Value = item.hdrTmpDriveType.ToByteArray();
                _updateParam26.Value = now.uniqueTime;
                _updateParam27.Value = now.uniqueTime;
                var count = conn.ExecuteNonQuery(_updateCommand);
                if (count > 0)
                {
                    item.modified = now;
                }
                return count;
            } // Using
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
        internal int TestTouch(Guid driveId, Guid fileId)
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