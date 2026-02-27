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
    public record MySubscriptionsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public OdinId sourceOwnerOdinId { get; set; }
        public Guid? sourceDriveId { get; set; }
        public Guid? sourceDriveTypeId { get; set; }
        public Guid? targetDriveId { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            sourceDriveId.AssertGuidNotEmpty("Guid parameter sourceDriveId cannot be set to Empty GUID.");
            sourceDriveTypeId.AssertGuidNotEmpty("Guid parameter sourceDriveTypeId cannot be set to Empty GUID.");
            targetDriveId.AssertGuidNotEmpty("Guid parameter targetDriveId cannot be set to Empty GUID.");
        }
    } // End of record MySubscriptionsRecord

    public abstract class TableMySubscriptionsCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "MySubscriptions";

        protected TableMySubscriptionsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


       /*
        * This method is no longer used.
        * It is kept here, commented-out, so you can see how the table is created without having to locate its latest migration.
        *
        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "MySubscriptions");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE MySubscriptions IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS MySubscriptions( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"sourceOwnerOdinId TEXT NOT NULL, "
                   +"sourceDriveId BYTEA , "
                   +"sourceDriveTypeId BYTEA , "
                   +"targetDriveId BYTEA , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0MySubscriptions ON MySubscriptions(identityId,sourceOwnerOdinId);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "MySubscriptions", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(MySubscriptionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO MySubscriptions (identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified) " +
                                           $"VALUES (@identityId,@sourceOwnerOdinId,@sourceDriveId,@sourceDriveTypeId,@targetDriveId,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@sourceOwnerOdinId", DbType.String, item.sourceOwnerOdinId.DomainName);
                insertCommand.AddParameter("@sourceDriveId", DbType.Binary, item.sourceDriveId);
                insertCommand.AddParameter("@sourceDriveTypeId", DbType.Binary, item.sourceDriveTypeId);
                insertCommand.AddParameter("@targetDriveId", DbType.Binary, item.targetDriveId);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(MySubscriptionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO MySubscriptions (identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified) " +
                                            $"VALUES (@identityId,@sourceOwnerOdinId,@sourceDriveId,@sourceDriveTypeId,@targetDriveId,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@sourceOwnerOdinId", DbType.String, item.sourceOwnerOdinId.DomainName);
                insertCommand.AddParameter("@sourceDriveId", DbType.Binary, item.sourceDriveId);
                insertCommand.AddParameter("@sourceDriveTypeId", DbType.Binary, item.sourceDriveTypeId);
                insertCommand.AddParameter("@targetDriveId", DbType.Binary, item.targetDriveId);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(MySubscriptionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO MySubscriptions (identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified) " +
                                            $"VALUES (@identityId,@sourceOwnerOdinId,@sourceDriveId,@sourceDriveTypeId,@targetDriveId,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId) DO UPDATE "+
                                            $"SET modified = {upsertCommand.SqlMax()}(MySubscriptions.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@sourceOwnerOdinId", DbType.String, item.sourceOwnerOdinId.DomainName);
                upsertCommand.AddParameter("@sourceDriveId", DbType.Binary, item.sourceDriveId);
                upsertCommand.AddParameter("@sourceDriveTypeId", DbType.Binary, item.sourceDriveTypeId);
                upsertCommand.AddParameter("@targetDriveId", DbType.Binary, item.targetDriveId);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(MySubscriptionsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE MySubscriptions " +
                                            $"SET modified = {updateCommand.SqlMax()}(MySubscriptions.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND sourceOwnerOdinId = @sourceOwnerOdinId AND sourceDriveId = @sourceDriveId AND sourceDriveTypeId = @sourceDriveTypeId AND targetDriveId = @targetDriveId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@sourceOwnerOdinId", DbType.String, item.sourceOwnerOdinId.DomainName);
                updateCommand.AddParameter("@sourceDriveId", DbType.Binary, item.sourceDriveId);
                updateCommand.AddParameter("@sourceDriveTypeId", DbType.Binary, item.sourceDriveTypeId);
                updateCommand.AddParameter("@targetDriveId", DbType.Binary, item.targetDriveId);
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    item.created = new UnixTimeUtc(created);
                    long modified = (long) rdr[1];
                    item.modified = new UnixTimeUtc((long)modified);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM MySubscriptions;";
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
            sl.Add("sourceOwnerOdinId");
            sl.Add("sourceDriveId");
            sl.Add("sourceDriveTypeId");
            sl.Add("targetDriveId");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified
        protected MySubscriptionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<MySubscriptionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new MySubscriptionsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.sourceOwnerOdinId = (rdr[2] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.sourceDriveId = (rdr[3] == DBNull.Value) ? null : new Guid((byte[])rdr[3]);
            item.sourceDriveTypeId = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.targetDriveId = (rdr[5] == DBNull.Value) ? null : new Guid((byte[])rdr[5]);
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        protected virtual async Task<int> DeleteBySourceOwnerOdinIdAsync(Guid identityId,OdinId sourceOwnerOdinId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM MySubscriptions " +
                                             "WHERE identityId = @identityId AND sourceOwnerOdinId = @sourceOwnerOdinId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@sourceOwnerOdinId", DbType.String, sourceOwnerOdinId.DomainName);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<int> DeleteAsync(Guid identityId,OdinId sourceOwnerOdinId,Guid sourceDriveId,Guid sourceDriveTypeId,Guid targetDriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete1Command = cn.CreateCommand();
            {
                delete1Command.CommandText = "DELETE FROM MySubscriptions " +
                                             "WHERE identityId = @identityId AND sourceOwnerOdinId = @sourceOwnerOdinId AND sourceDriveId = @sourceDriveId AND sourceDriveTypeId = @sourceDriveTypeId AND targetDriveId = @targetDriveId";

                delete1Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete1Command.AddParameter("@sourceOwnerOdinId", DbType.String, sourceOwnerOdinId.DomainName);
                delete1Command.AddParameter("@sourceDriveId", DbType.Binary, sourceDriveId);
                delete1Command.AddParameter("@sourceDriveTypeId", DbType.Binary, sourceDriveTypeId);
                delete1Command.AddParameter("@targetDriveId", DbType.Binary, targetDriveId);
                var count = await delete1Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<MySubscriptionsRecord> PopAsync(Guid identityId,OdinId sourceOwnerOdinId,Guid sourceDriveId,Guid sourceDriveTypeId,Guid targetDriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM MySubscriptions " +
                                             "WHERE identityId = @identityId AND sourceOwnerOdinId = @sourceOwnerOdinId AND sourceDriveId = @sourceDriveId AND sourceDriveTypeId = @sourceDriveTypeId AND targetDriveId = @targetDriveId " + 
                                             "RETURNING rowId,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@sourceOwnerOdinId", DbType.String, sourceOwnerOdinId.DomainName);
                deleteCommand.AddParameter("@sourceDriveId", DbType.Binary, sourceDriveId);
                deleteCommand.AddParameter("@sourceDriveTypeId", DbType.Binary, sourceDriveTypeId);
                deleteCommand.AddParameter("@targetDriveId", DbType.Binary, targetDriveId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected MySubscriptionsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,OdinId sourceOwnerOdinId,Guid? sourceDriveId,Guid? sourceDriveTypeId,Guid? targetDriveId)
        {
            var result = new List<MySubscriptionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new MySubscriptionsRecord();
            item.identityId = identityId;
            item.sourceOwnerOdinId = sourceOwnerOdinId;
            item.sourceDriveId = sourceDriveId;
            item.sourceDriveTypeId = sourceDriveTypeId;
            item.targetDriveId = targetDriveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.created = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[1]);
            item.modified = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            return item;
       }

        protected virtual async Task<MySubscriptionsRecord> GetAsync(Guid identityId,OdinId sourceOwnerOdinId,Guid sourceDriveId,Guid sourceDriveTypeId,Guid targetDriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,created,modified FROM MySubscriptions " +
                                             "WHERE identityId = @identityId AND sourceOwnerOdinId = @sourceOwnerOdinId AND sourceDriveId = @sourceDriveId AND sourceDriveTypeId = @sourceDriveTypeId AND targetDriveId = @targetDriveId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@sourceOwnerOdinId", DbType.String, sourceOwnerOdinId.DomainName);
                get0Command.AddParameter("@sourceDriveId", DbType.Binary, sourceDriveId);
                get0Command.AddParameter("@sourceDriveTypeId", DbType.Binary, sourceDriveTypeId);
                get0Command.AddParameter("@targetDriveId", DbType.Binary, targetDriveId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected MySubscriptionsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<MySubscriptionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new MySubscriptionsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.sourceOwnerOdinId = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.sourceDriveId = (rdr[2] == DBNull.Value) ? null : new Guid((byte[])rdr[2]);
            item.sourceDriveTypeId = (rdr[3] == DBNull.Value) ? null : new Guid((byte[])rdr[3]);
            item.targetDriveId = (rdr[4] == DBNull.Value) ? null : new Guid((byte[])rdr[4]);
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<List<MySubscriptionsRecord>> GetAllAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,sourceOwnerOdinId,sourceDriveId,sourceDriveTypeId,targetDriveId,created,modified FROM MySubscriptions " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<MySubscriptionsRecord>();
                        }
                        var result = new List<MySubscriptionsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

    }
}
