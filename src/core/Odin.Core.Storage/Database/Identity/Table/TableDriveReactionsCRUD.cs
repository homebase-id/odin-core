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
using Odin.Core.Storage;
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

    public abstract class TableDriveReactionsCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "DriveReactions";

        protected TableDriveReactionsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "DriveReactions");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
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
            await SqlHelper.CreateTableWithCommentAsync(cn, "DriveReactions", createSql, commentSql);
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
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@postId", DbType.Binary, item.postId);
                insertCommand.AddParameter("@identity", DbType.String, item.identity.DomainName);
                insertCommand.AddParameter("@singleReaction", DbType.String, item.singleReaction);
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
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                insertCommand.AddParameter("@postId", DbType.Binary, item.postId);
                insertCommand.AddParameter("@identity", DbType.String, item.identity.DomainName);
                insertCommand.AddParameter("@singleReaction", DbType.String, item.singleReaction);
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
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                upsertCommand.AddParameter("@postId", DbType.Binary, item.postId);
                upsertCommand.AddParameter("@identity", DbType.String, item.identity.DomainName);
                upsertCommand.AddParameter("@singleReaction", DbType.String, item.singleReaction);
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
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@driveId", DbType.Binary, item.driveId);
                updateCommand.AddParameter("@postId", DbType.Binary, item.postId);
                updateCommand.AddParameter("@identity", DbType.String, item.identity.DomainName);
                updateCommand.AddParameter("@singleReaction", DbType.String, item.singleReaction);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected new async Task<int> GetCountAsync()
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

        public new static List<string> GetColumnNames()
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
                getCountDriveCommand.AddParameter("$driveId", DbType.Binary, driveId);
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

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete0Command.AddParameter("@identity", DbType.String, identity.DomainName);
                delete0Command.AddParameter("@postId", DbType.Binary, postId);
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

                delete1Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete1Command.AddParameter("@driveId", DbType.Binary, driveId);
                delete1Command.AddParameter("@postId", DbType.Binary, postId);
                delete1Command.AddParameter("@identity", DbType.String, identity.DomainName);
                delete1Command.AddParameter("@singleReaction", DbType.String, singleReaction);
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

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@driveId", DbType.Binary, driveId);
                deleteCommand.AddParameter("@postId", DbType.Binary, postId);
                deleteCommand.AddParameter("@identity", DbType.String, identity.DomainName);
                deleteCommand.AddParameter("@singleReaction", DbType.String, singleReaction);
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

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@driveId", DbType.Binary, driveId);
                get0Command.AddParameter("@postId", DbType.Binary, postId);
                get0Command.AddParameter("@identity", DbType.String, identity.DomainName);
                get0Command.AddParameter("@singleReaction", DbType.String, singleReaction);
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

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

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
