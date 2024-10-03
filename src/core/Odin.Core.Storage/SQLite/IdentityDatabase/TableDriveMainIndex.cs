using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
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

        public void RecreateTable()
        {
            using var conn = _db.CreateDisposableConnection();
            EnsureTableExists(conn, true);
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
        
        // REMOVED TransferHistory and ReactionUpdate by hand
        public virtual int UpsertAllButReactionsAndTransfer(DatabaseConnection conn, DriveMainIndexRecord item)
        {
            using (var _upsertCommand = _database.CreateCommand())
            {
                _upsertCommand.CommandText = "INSERT INTO driveMainIndex (identityId,driveId,fileId,globalTransitId,fileState,requiredSecurityGroup,fileSystemType,userDate,fileType,dataType,archivalStatus,historyStatus,senderId,groupId,uniqueId,byteCount,hdrEncryptedKeyHeader,hdrVersionTag,hdrAppData,hdrServerData,hdrFileMetaData,hdrTmpDriveAlias,hdrTmpDriveType,created) " +
                                             "VALUES (@identityId,@driveId,@fileId,@globalTransitId,@fileState,@requiredSecurityGroup,@fileSystemType,@userDate,@fileType,@dataType,@archivalStatus,@historyStatus,@senderId,@groupId,@uniqueId,@byteCount,@hdrEncryptedKeyHeader,@hdrVersionTag,@hdrAppData,@hdrServerData,@hdrFileMetaData,@hdrTmpDriveAlias,@hdrTmpDriveType,@created)" +
                                             "ON CONFLICT (identityId,driveId,fileId) DO UPDATE " +
                                             "SET globalTransitId = @globalTransitId,fileState = @fileState,requiredSecurityGroup = @requiredSecurityGroup,fileSystemType = @fileSystemType,userDate = @userDate,fileType = @fileType,dataType = @dataType,archivalStatus = @archivalStatus,historyStatus = @historyStatus,senderId = @senderId,groupId = @groupId,uniqueId = @uniqueId,byteCount = @byteCount,hdrEncryptedKeyHeader = @hdrEncryptedKeyHeader,hdrVersionTag = @hdrVersionTag,hdrAppData = @hdrAppData,hdrServerData = @hdrServerData,hdrFileMetaData = @hdrFileMetaData,hdrTmpDriveAlias = @hdrTmpDriveAlias,hdrTmpDriveType = @hdrTmpDriveType,modified = @modified " +
                                             "RETURNING created, modified;";
                var _upsertParam1 = _upsertCommand.CreateParameter();
                _upsertParam1.ParameterName = "@identityId";
                _upsertCommand.Parameters.Add(_upsertParam1);
                var _upsertParam2 = _upsertCommand.CreateParameter();
                _upsertParam2.ParameterName = "@driveId";
                _upsertCommand.Parameters.Add(_upsertParam2);
                var _upsertParam3 = _upsertCommand.CreateParameter();
                _upsertParam3.ParameterName = "@fileId";
                _upsertCommand.Parameters.Add(_upsertParam3);
                var _upsertParam4 = _upsertCommand.CreateParameter();
                _upsertParam4.ParameterName = "@globalTransitId";
                _upsertCommand.Parameters.Add(_upsertParam4);
                var _upsertParam5 = _upsertCommand.CreateParameter();
                _upsertParam5.ParameterName = "@fileState";
                _upsertCommand.Parameters.Add(_upsertParam5);
                var _upsertParam6 = _upsertCommand.CreateParameter();
                _upsertParam6.ParameterName = "@requiredSecurityGroup";
                _upsertCommand.Parameters.Add(_upsertParam6);
                var _upsertParam7 = _upsertCommand.CreateParameter();
                _upsertParam7.ParameterName = "@fileSystemType";
                _upsertCommand.Parameters.Add(_upsertParam7);
                var _upsertParam8 = _upsertCommand.CreateParameter();
                _upsertParam8.ParameterName = "@userDate";
                _upsertCommand.Parameters.Add(_upsertParam8);
                var _upsertParam9 = _upsertCommand.CreateParameter();
                _upsertParam9.ParameterName = "@fileType";
                _upsertCommand.Parameters.Add(_upsertParam9);
                var _upsertParam10 = _upsertCommand.CreateParameter();
                _upsertParam10.ParameterName = "@dataType";
                _upsertCommand.Parameters.Add(_upsertParam10);
                var _upsertParam11 = _upsertCommand.CreateParameter();
                _upsertParam11.ParameterName = "@archivalStatus";
                _upsertCommand.Parameters.Add(_upsertParam11);
                var _upsertParam12 = _upsertCommand.CreateParameter();
                _upsertParam12.ParameterName = "@historyStatus";
                _upsertCommand.Parameters.Add(_upsertParam12);
                var _upsertParam13 = _upsertCommand.CreateParameter();
                _upsertParam13.ParameterName = "@senderId";
                _upsertCommand.Parameters.Add(_upsertParam13);
                var _upsertParam14 = _upsertCommand.CreateParameter();
                _upsertParam14.ParameterName = "@groupId";
                _upsertCommand.Parameters.Add(_upsertParam14);
                var _upsertParam15 = _upsertCommand.CreateParameter();
                _upsertParam15.ParameterName = "@uniqueId";
                _upsertCommand.Parameters.Add(_upsertParam15);
                var _upsertParam16 = _upsertCommand.CreateParameter();
                _upsertParam16.ParameterName = "@byteCount";
                _upsertCommand.Parameters.Add(_upsertParam16);
                var _upsertParam17 = _upsertCommand.CreateParameter();
                _upsertParam17.ParameterName = "@hdrEncryptedKeyHeader";
                _upsertCommand.Parameters.Add(_upsertParam17);
                var _upsertParam18 = _upsertCommand.CreateParameter();
                _upsertParam18.ParameterName = "@hdrVersionTag";
                _upsertCommand.Parameters.Add(_upsertParam18);
                var _upsertParam19 = _upsertCommand.CreateParameter();
                _upsertParam19.ParameterName = "@hdrAppData";
                _upsertCommand.Parameters.Add(_upsertParam19);
                var _upsertParam20 = _upsertCommand.CreateParameter();
                _upsertParam20.ParameterName = "@hdrReactionSummary";
                _upsertCommand.Parameters.Add(_upsertParam20);
                var _upsertParam21 = _upsertCommand.CreateParameter();
                _upsertParam21.ParameterName = "@hdrServerData";
                _upsertCommand.Parameters.Add(_upsertParam21);
                var _upsertParam22 = _upsertCommand.CreateParameter();
                _upsertParam22.ParameterName = "@hdrTransferHistory";
                _upsertCommand.Parameters.Add(_upsertParam22);
                var _upsertParam23 = _upsertCommand.CreateParameter();
                _upsertParam23.ParameterName = "@hdrFileMetaData";
                _upsertCommand.Parameters.Add(_upsertParam23);
                var _upsertParam24 = _upsertCommand.CreateParameter();
                _upsertParam24.ParameterName = "@hdrTmpDriveAlias";
                _upsertCommand.Parameters.Add(_upsertParam24);
                var _upsertParam25 = _upsertCommand.CreateParameter();
                _upsertParam25.ParameterName = "@hdrTmpDriveType";
                _upsertCommand.Parameters.Add(_upsertParam25);
                var _upsertParam26 = _upsertCommand.CreateParameter();
                _upsertParam26.ParameterName = "@created";
                _upsertCommand.Parameters.Add(_upsertParam26);
                var _upsertParam27 = _upsertCommand.CreateParameter();
                _upsertParam27.ParameterName = "@modified";
                _upsertCommand.Parameters.Add(_upsertParam27);
                var now = UnixTimeUtcUnique.Now();
                _upsertParam1.Value = item.identityId.ToByteArray();
                _upsertParam2.Value = item.driveId.ToByteArray();
                _upsertParam3.Value = item.fileId.ToByteArray();
                _upsertParam4.Value = item.globalTransitId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam5.Value = item.fileState;
                _upsertParam6.Value = item.requiredSecurityGroup;
                _upsertParam7.Value = item.fileSystemType;
                _upsertParam8.Value = item.userDate.milliseconds;
                _upsertParam9.Value = item.fileType;
                _upsertParam10.Value = item.dataType;
                _upsertParam11.Value = item.archivalStatus;
                _upsertParam12.Value = item.historyStatus;
                _upsertParam13.Value = item.senderId ?? (object)DBNull.Value;
                _upsertParam14.Value = item.groupId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam15.Value = item.uniqueId?.ToByteArray() ?? (object)DBNull.Value;
                _upsertParam16.Value = item.byteCount;
                _upsertParam17.Value = item.hdrEncryptedKeyHeader;
                _upsertParam18.Value = item.hdrVersionTag.ToByteArray();
                _upsertParam19.Value = item.hdrAppData;
                _upsertParam20.Value = item.hdrReactionSummary ?? (object)DBNull.Value;
                _upsertParam21.Value = item.hdrServerData;
                _upsertParam22.Value = item.hdrTransferHistory ?? (object)DBNull.Value;
                _upsertParam23.Value = item.hdrFileMetaData;
                _upsertParam24.Value = item.hdrTmpDriveAlias.ToByteArray();
                _upsertParam25.Value = item.hdrTmpDriveType.ToByteArray();
                _upsertParam26.Value = now.uniqueTime;
                _upsertParam27.Value = now.uniqueTime;
                using (SqliteDataReader rdr = conn.ExecuteReader(_upsertCommand, System.Data.CommandBehavior.SingleRow))
                {
                    if (rdr.Read())
                    {
                        long created = rdr.GetInt64(0);
                        long? modified = rdr.IsDBNull(1) ? null : rdr.GetInt64(1);
                        item.created = new UnixTimeUtcUnique(created);
                        if (modified != null)
                            item.modified = new UnixTimeUtcUnique((long)modified);
                        else
                            item.modified = null;
                        return 1;
                    }
                }
                return 0;
            } // Using
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

        public async Task<List<DriveMainIndexRecord>> GetAll()
        {
            using var cn = _db.CreateDisposableConnection();
            var records = await cn.Connection.QueryAsync<DriveMainIndexRecord>(
                $"SELECT * FROM {_db.tblDriveMainIndex._tableName} order by fileId");
            return records.AsList();
        }
   }
}