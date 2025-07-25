using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite; //added for homebase social sync

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveReactionsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid driveId { get; set; }
        public Guid postId { get; set; }
        public OdinId identity { get; set; }
        public string singleReaction { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            driveId.AssertGuidNotEmpty("Guid parameter driveId cannot be set to Empty GUID.");
            postId.AssertGuidNotEmpty("Guid parameter postId cannot be set to Empty GUID.");
            if (singleReaction == null) throw new OdinDatabaseValidationException("Cannot be null singleReaction");
            if (singleReaction?.Length < 3) throw new OdinDatabaseValidationException($"Too short singleReaction, was {singleReaction.Length} (min 3)");
            if (singleReaction?.Length > 80) throw new OdinDatabaseValidationException($"Too long singleReaction, was {singleReaction.Length} (max 80)");
        }
    } // End of record DriveReactionsRecord

    public abstract class TableDriveReactionsCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveReactionsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "DriveReactions");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DriveReactions IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DriveReactions( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"driveId BYTEA NOT NULL, "
                   +"postId BYTEA NOT NULL, "
                   +"identity TEXT NOT NULL, "
                   +"singleReaction TEXT NOT NULL "
                   +", UNIQUE(identityId,driveId,postId,identity,singleReaction)"
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(DriveReactionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveReactions (identityId,driveId,postId,identity,singleReaction) " +
                                           $"VALUES (@identityId,@driveId,@postId,@identity,@singleReaction)"+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@postId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@singleReaction";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.postId.ToByteArray();
                insertParam4.Value = item.identity.DomainName;
                insertParam5.Value = item.singleReaction;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveReactionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO DriveReactions (identityId,driveId,postId,identity,singleReaction) " +
                                            $"VALUES (@identityId,@driveId,@postId,@identity,@singleReaction) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.Binary;
                insertParam2.ParameterName = "@driveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.Binary;
                insertParam3.ParameterName = "@postId";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@identity";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.String;
                insertParam5.ParameterName = "@singleReaction";
                insertCommand.Parameters.Add(insertParam5);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.driveId.ToByteArray();
                insertParam3.Value = item.postId.ToByteArray();
                insertParam4.Value = item.identity.DomainName;
                insertParam5.Value = item.singleReaction;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveReactionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO DriveReactions (identityId,driveId,postId,identity,singleReaction) " +
                                            $"VALUES (@identityId,@driveId,@postId,@identity,@singleReaction)"+
                                            "ON CONFLICT (identityId,driveId,postId,identity,singleReaction) DO UPDATE "+
                                            $"SET  "+
                                            "RETURNING -1,-1,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.Binary;
                upsertParam2.ParameterName = "@driveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.Binary;
                upsertParam3.ParameterName = "@postId";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@identity";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.String;
                upsertParam5.ParameterName = "@singleReaction";
                upsertCommand.Parameters.Add(upsertParam5);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.driveId.ToByteArray();
                upsertParam3.Value = item.postId.ToByteArray();
                upsertParam4.Value = item.identity.DomainName;
                upsertParam5.Value = item.singleReaction;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveReactionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE DriveReactions " +
                                            $"SET  "+
                                            "WHERE (identityId = @identityId AND driveId = @driveId AND postId = @postId AND identity = @identity AND singleReaction = @singleReaction) "+
                                            "RETURNING -1,-1,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.Binary;
                updateParam2.ParameterName = "@driveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.Binary;
                updateParam3.ParameterName = "@postId";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@identity";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.String;
                updateParam5.ParameterName = "@singleReaction";
                updateCommand.Parameters.Add(updateParam5);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.driveId.ToByteArray();
                updateParam3.Value = item.postId.ToByteArray();
                updateParam4.Value = item.identity.DomainName;
                updateParam5.Value = item.singleReaction;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveReactions;";
                var count = await getCountCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public static List<string> GetColumnNames()
        {
            var sl = new List<string>();
            sl.Add("rowId");
            sl.Add("identityId");
            sl.Add("driveId");
            sl.Add("postId");
            sl.Add("identity");
            sl.Add("singleReaction");
            return sl;
        }

        protected virtual async Task<int> GetDriveCountAsync(Guid driveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountDriveCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountDriveCommand.CommandText = "SELECT COUNT(*) FROM DriveReactions WHERE driveId = $driveId;";
                var getCountDriveParam1 = getCountDriveCommand.CreateParameter();
                getCountDriveParam1.DbType = DbType.Binary;
                getCountDriveParam1.ParameterName = "$driveId";
                getCountDriveCommand.Parameters.Add(getCountDriveParam1);
                getCountDriveParam1.Value = driveId.ToByteArray();
                var count = await getCountDriveCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            } // using
        }

        // SELECT rowId,identityId,driveId,postId,identity,singleReaction
        protected DriveReactionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveReactionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveReactionsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.driveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.postId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.identity = (rdr[4] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[4]);
            item.singleReaction = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            return item;
       }

        protected virtual async Task<int> DeleteAllReactionsAsync(Guid identityId,Guid driveId,OdinId identity,Guid postId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND identity = @identity AND postId = @postId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.DbType = DbType.Binary;
                delete0Param2.ParameterName = "@driveId";
                delete0Command.Parameters.Add(delete0Param2);
                var delete0Param3 = delete0Command.CreateParameter();
                delete0Param3.DbType = DbType.String;
                delete0Param3.ParameterName = "@identity";
                delete0Command.Parameters.Add(delete0Param3);
                var delete0Param4 = delete0Command.CreateParameter();
                delete0Param4.DbType = DbType.Binary;
                delete0Param4.ParameterName = "@postId";
                delete0Command.Parameters.Add(delete0Param4);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = driveId.ToByteArray();
                delete0Param3.Value = identity.DomainName;
                delete0Param4.Value = postId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid driveId,Guid postId,OdinId identity,string singleReaction)
        {
            if (singleReaction == null) throw new OdinDatabaseValidationException("Cannot be null singleReaction");
            if (singleReaction?.Length < 3) throw new OdinDatabaseValidationException($"Too short singleReaction, was {singleReaction.Length} (min 3)");
            if (singleReaction?.Length > 80) throw new OdinDatabaseValidationException($"Too long singleReaction, was {singleReaction.Length} (max 80)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM DriveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND postId = @postId AND identity = @identity AND singleReaction = @singleReaction";
                var delete1Param1 = delete1Command.CreateParameter();
                delete1Param1.DbType = DbType.Binary;
                delete1Param1.ParameterName = "@identityId";
                delete1Command.Parameters.Add(delete1Param1);
                var delete1Param2 = delete1Command.CreateParameter();
                delete1Param2.DbType = DbType.Binary;
                delete1Param2.ParameterName = "@driveId";
                delete1Command.Parameters.Add(delete1Param2);
                var delete1Param3 = delete1Command.CreateParameter();
                delete1Param3.DbType = DbType.Binary;
                delete1Param3.ParameterName = "@postId";
                delete1Command.Parameters.Add(delete1Param3);
                var delete1Param4 = delete1Command.CreateParameter();
                delete1Param4.DbType = DbType.String;
                delete1Param4.ParameterName = "@identity";
                delete1Command.Parameters.Add(delete1Param4);
                var delete1Param5 = delete1Command.CreateParameter();
                delete1Param5.DbType = DbType.String;
                delete1Param5.ParameterName = "@singleReaction";
                delete1Command.Parameters.Add(delete1Param5);

                delete1Param1.Value = identityId.ToByteArray();
                delete1Param2.Value = driveId.ToByteArray();
                delete1Param3.Value = postId.ToByteArray();
                delete1Param4.Value = identity.DomainName;
                delete1Param5.Value = singleReaction;
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<DriveReactionsRecord> PopAsync(Guid identityId,Guid driveId,Guid postId,OdinId identity,string singleReaction)
        {
            if (singleReaction == null) throw new OdinDatabaseValidationException("Cannot be null singleReaction");
            if (singleReaction?.Length < 3) throw new OdinDatabaseValidationException($"Too short singleReaction, was {singleReaction.Length} (min 3)");
            if (singleReaction?.Length > 80) throw new OdinDatabaseValidationException($"Too long singleReaction, was {singleReaction.Length} (max 80)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM DriveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND postId = @postId AND identity = @identity AND singleReaction = @singleReaction " + 
                                             "RETURNING rowId";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);
                var deleteParam2 = deleteCommand.CreateParameter();
                deleteParam2.DbType = DbType.Binary;
                deleteParam2.ParameterName = "@driveId";
                deleteCommand.Parameters.Add(deleteParam2);
                var deleteParam3 = deleteCommand.CreateParameter();
                deleteParam3.DbType = DbType.Binary;
                deleteParam3.ParameterName = "@postId";
                deleteCommand.Parameters.Add(deleteParam3);
                var deleteParam4 = deleteCommand.CreateParameter();
                deleteParam4.DbType = DbType.String;
                deleteParam4.ParameterName = "@identity";
                deleteCommand.Parameters.Add(deleteParam4);
                var deleteParam5 = deleteCommand.CreateParameter();
                deleteParam5.DbType = DbType.String;
                deleteParam5.ParameterName = "@singleReaction";
                deleteCommand.Parameters.Add(deleteParam5);

                deleteParam1.Value = identityId.ToByteArray();
                deleteParam2.Value = driveId.ToByteArray();
                deleteParam3.Value = postId.ToByteArray();
                deleteParam4.Value = identity.DomainName;
                deleteParam5.Value = singleReaction;
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,driveId,postId,identity,singleReaction);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected DriveReactionsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid driveId,Guid postId,OdinId identity,string singleReaction)
        {
            if (singleReaction == null) throw new OdinDatabaseValidationException("Cannot be null singleReaction");
            if (singleReaction?.Length < 3) throw new OdinDatabaseValidationException($"Too short singleReaction, was {singleReaction.Length} (min 3)");
            if (singleReaction?.Length > 80) throw new OdinDatabaseValidationException($"Too long singleReaction, was {singleReaction.Length} (max 80)");
            var result = new List<DriveReactionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveReactionsRecord();
            item.identityId = identityId;
            item.driveId = driveId;
            item.postId = postId;
            item.identity = identity;
            item.singleReaction = singleReaction;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            return item;
       }

        protected virtual async Task<DriveReactionsRecord> GetAsync(Guid identityId,Guid driveId,Guid postId,OdinId identity,string singleReaction)
        {
            if (singleReaction == null) throw new OdinDatabaseValidationException("Cannot be null singleReaction");
            if (singleReaction?.Length < 3) throw new OdinDatabaseValidationException($"Too short singleReaction, was {singleReaction.Length} (min 3)");
            if (singleReaction?.Length > 80) throw new OdinDatabaseValidationException($"Too long singleReaction, was {singleReaction.Length} (max 80)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId FROM DriveReactions " +
                                             "WHERE identityId = @identityId AND driveId = @driveId AND postId = @postId AND identity = @identity AND singleReaction = @singleReaction LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.DbType = DbType.Binary;
                get0Param2.ParameterName = "@driveId";
                get0Command.Parameters.Add(get0Param2);
                var get0Param3 = get0Command.CreateParameter();
                get0Param3.DbType = DbType.Binary;
                get0Param3.ParameterName = "@postId";
                get0Command.Parameters.Add(get0Param3);
                var get0Param4 = get0Command.CreateParameter();
                get0Param4.DbType = DbType.String;
                get0Param4.ParameterName = "@identity";
                get0Command.Parameters.Add(get0Param4);
                var get0Param5 = get0Command.CreateParameter();
                get0Param5.DbType = DbType.String;
                get0Param5.ParameterName = "@singleReaction";
                get0Command.Parameters.Add(get0Param5);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = driveId.ToByteArray();
                get0Param3.Value = postId.ToByteArray();
                get0Param4.Value = identity.DomainName;
                get0Param5.Value = singleReaction;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,driveId,postId,identity,singleReaction);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveReactionsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = 0;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging0Command = cn.CreateCommand();
            {
                getPaging0Command.CommandText = "SELECT rowId,identityId,driveId,postId,identity,singleReaction FROM DriveReactions " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";
                var getPaging0Param1 = getPaging0Command.CreateParameter();
                getPaging0Param1.DbType = DbType.Int64;
                getPaging0Param1.ParameterName = "@rowId";
                getPaging0Command.Parameters.Add(getPaging0Param1);
                var getPaging0Param2 = getPaging0Command.CreateParameter();
                getPaging0Param2.DbType = DbType.Int64;
                getPaging0Param2.ParameterName = "@count";
                getPaging0Command.Parameters.Add(getPaging0Param2);

                getPaging0Param1.Value = inCursor;
                getPaging0Param2.Value = count+1;

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveReactionsRecord>();
                        Int64? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                        }
                        return (result, nextCursor);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
