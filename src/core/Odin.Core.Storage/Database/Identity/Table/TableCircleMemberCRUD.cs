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
    public record CircleMemberRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid circleId { get; set; }
        public Guid memberId { get; set; }
        public byte[] data { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            circleId.AssertGuidNotEmpty("Guid parameter circleId cannot be set to Empty GUID.");
            memberId.AssertGuidNotEmpty("Guid parameter memberId cannot be set to Empty GUID.");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 65535) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 65535)");
        }
    } // End of record CircleMemberRecord

    public abstract class TableCircleMemberCRUD : TableBase
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "CircleMember";

        protected TableCircleMemberCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "CircleMember");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE CircleMember IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS CircleMember( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"circleId BYTEA NOT NULL, "
                   +"memberId BYTEA NOT NULL, "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,circleId,memberId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "CircleMember", createSql, commentSql);
        }
       */

        protected virtual async Task<int> InsertAsync(CircleMemberRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO CircleMember (identityId,circleId,memberId,data) " +
                                           $"VALUES (@identityId,@circleId,@memberId,@data)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                insertCommand.AddParameter("@memberId", DbType.Binary, item.memberId);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(CircleMemberRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO CircleMember (identityId,circleId,memberId,data) " +
                                            $"VALUES (@identityId,@circleId,@memberId,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                insertCommand.AddParameter("@memberId", DbType.Binary, item.memberId);
                insertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(CircleMemberRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO CircleMember (identityId,circleId,memberId,data) " +
                                            $"VALUES (@identityId,@circleId,@memberId,@data)"+
                                            "ON CONFLICT (identityId,circleId,memberId) DO UPDATE "+
                                            $"SET data = @data "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                upsertCommand.AddParameter("@memberId", DbType.Binary, item.memberId);
                upsertCommand.AddParameter("@data", DbType.Binary, item.data);
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(CircleMemberRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE CircleMember " +
                                            $"SET data = @data "+
                                            "WHERE (identityId = @identityId AND circleId = @circleId AND memberId = @memberId) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@circleId", DbType.Binary, item.circleId);
                updateCommand.AddParameter("@memberId", DbType.Binary, item.memberId);
                updateCommand.AddParameter("@data", DbType.Binary, item.data);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM CircleMember;";
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
            sl.Add("circleId");
            sl.Add("memberId");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,circleId,memberId,data
        protected CircleMemberRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.circleId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.memberId = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.data = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM CircleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@circleId", DbType.Binary, circleId);
                delete0Command.AddParameter("@memberId", DbType.Binary, memberId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<CircleMemberRecord> PopAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM CircleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId " + 
                                             "RETURNING rowId,data";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@circleId", DbType.Binary, circleId);
                deleteCommand.AddParameter("@memberId", DbType.Binary, memberId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,circleId,memberId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected CircleMemberRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid circleId,Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.memberId = memberId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.data = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<CircleMemberRecord> GetAsync(Guid identityId,Guid circleId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,data FROM CircleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId AND memberId = @memberId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@circleId", DbType.Binary, circleId);
                get0Command.AddParameter("@memberId", DbType.Binary, memberId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,circleId,memberId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected CircleMemberRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.memberId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.data = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<CircleMemberRecord>> GetAllCirclesAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,circleId,memberId,data FROM CircleMember " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<CircleMemberRecord>();
                        }
                        var result = new List<CircleMemberRecord>();
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

        protected CircleMemberRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid circleId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.circleId = circleId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.memberId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<CircleMemberRecord>> GetCircleMembersAsync(Guid identityId,Guid circleId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,memberId,data FROM CircleMember " +
                                             "WHERE identityId = @identityId AND circleId = @circleId "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@circleId", DbType.Binary, circleId);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<CircleMemberRecord>();
                        }
                        var result = new List<CircleMemberRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId,circleId));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected CircleMemberRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid memberId)
        {
            var result = new List<CircleMemberRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CircleMemberRecord();
            item.identityId = identityId;
            item.memberId = memberId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.circleId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<CircleMemberRecord>> GetMemberCirclesAndDataAsync(Guid identityId,Guid memberId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,circleId,data FROM CircleMember " +
                                             "WHERE identityId = @identityId AND memberId = @memberId "+
                                             ";";

                get3Command.AddParameter("@identityId", DbType.Binary, identityId);
                get3Command.AddParameter("@memberId", DbType.Binary, memberId);
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<CircleMemberRecord>();
                        }
                        var result = new List<CircleMemberRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader3(rdr,identityId,memberId));
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
