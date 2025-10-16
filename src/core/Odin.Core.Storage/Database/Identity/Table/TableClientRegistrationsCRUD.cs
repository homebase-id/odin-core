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
    public record ClientRegistrationsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public Guid catId { get; set; }
        public OdinId issuedToId { get; set; }
        public UnixTimeUtc expiresAt { get; set; }
        public Int32 catType { get; set; }
        public string value { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            catId.AssertGuidNotEmpty("Guid parameter catId cannot be set to Empty GUID.");
            if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short value, was {value.Length} (min 0)");
            if (value?.Length > 131070) throw new OdinDatabaseValidationException($"Too long value, was {value.Length} (max 131070)");
        }
    } // End of record ClientRegistrationsRecord

    public abstract class TableClientRegistrationsCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "ClientRegistrations";

        protected TableClientRegistrationsCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "ClientRegistrations");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE ClientRegistrations IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS ClientRegistrations( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"catId BYTEA NOT NULL UNIQUE, "
                   +"issuedToId TEXT NOT NULL, "
                   +"expiresAt BIGINT NOT NULL, "
                   +"catType BIGINT NOT NULL, "
                   +"value TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(identityId,catId)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "ClientRegistrations", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(ClientRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO ClientRegistrations (identityId,catId,issuedToId,expiresAt,catType,value,created,modified) " +
                                           $"VALUES (@identityId,@catId,@issuedToId,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@catId", DbType.Binary, item.catId);
                insertCommand.AddParameter("@issuedToId", DbType.String, item.issuedToId.DomainName);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                insertCommand.AddParameter("@catType", DbType.Int32, item.catType);
                insertCommand.AddParameter("@value", DbType.String, item.value);
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

        protected virtual async Task<bool> TryInsertAsync(ClientRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO ClientRegistrations (identityId,catId,issuedToId,expiresAt,catType,value,created,modified) " +
                                            $"VALUES (@identityId,@catId,@issuedToId,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@catId", DbType.Binary, item.catId);
                insertCommand.AddParameter("@issuedToId", DbType.String, item.issuedToId.DomainName);
                insertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                insertCommand.AddParameter("@catType", DbType.Int32, item.catType);
                insertCommand.AddParameter("@value", DbType.String, item.value);
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

        protected virtual async Task<int> UpsertAsync(ClientRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO ClientRegistrations (identityId,catId,issuedToId,expiresAt,catType,value,created,modified) " +
                                            $"VALUES (@identityId,@catId,@issuedToId,@expiresAt,@catType,@value,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId,catId) DO UPDATE "+
                                            $"SET issuedToId = @issuedToId,expiresAt = @expiresAt,catType = @catType,value = @value,modified = {upsertCommand.SqlMax()}(ClientRegistrations.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@catId", DbType.Binary, item.catId);
                upsertCommand.AddParameter("@issuedToId", DbType.String, item.issuedToId.DomainName);
                upsertCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                upsertCommand.AddParameter("@catType", DbType.Int32, item.catType);
                upsertCommand.AddParameter("@value", DbType.String, item.value);
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

        protected virtual async Task<int> UpdateAsync(ClientRegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE ClientRegistrations " +
                                            $"SET issuedToId = @issuedToId,expiresAt = @expiresAt,catType = @catType,value = @value,modified = {updateCommand.SqlMax()}(ClientRegistrations.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId AND catId = @catId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@catId", DbType.Binary, item.catId);
                updateCommand.AddParameter("@issuedToId", DbType.String, item.issuedToId.DomainName);
                updateCommand.AddParameter("@expiresAt", DbType.Int64, item.expiresAt.milliseconds);
                updateCommand.AddParameter("@catType", DbType.Int32, item.catType);
                updateCommand.AddParameter("@value", DbType.String, item.value);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM ClientRegistrations;";
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
            sl.Add("catId");
            sl.Add("issuedToId");
            sl.Add("expiresAt");
            sl.Add("catType");
            sl.Add("value");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,catId,issuedToId,expiresAt,catType,value,created,modified
        protected ClientRegistrationsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<ClientRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ClientRegistrationsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.catId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.issuedToId = (rdr[3] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[3]);
            item.expiresAt = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.catType = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[5];
            item.value = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid catId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM ClientRegistrations " +
                                             "WHERE identityId = @identityId AND catId = @catId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@catId", DbType.Binary, catId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<ClientRegistrationsRecord> PopAsync(Guid identityId,Guid catId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM ClientRegistrations " +
                                             "WHERE identityId = @identityId AND catId = @catId " + 
                                             "RETURNING rowId,issuedToId,expiresAt,catType,value,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@catId", DbType.Binary, catId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,catId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected ClientRegistrationsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid catId)
        {
            var result = new List<ClientRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ClientRegistrationsRecord();
            item.identityId = identityId;
            item.catId = catId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.issuedToId = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.expiresAt = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.catType = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[3];
            item.value = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<ClientRegistrationsRecord> GetAsync(Guid identityId,Guid catId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,issuedToId,expiresAt,catType,value,created,modified FROM ClientRegistrations " +
                                             "WHERE identityId = @identityId AND catId = @catId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@catId", DbType.Binary, catId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,catId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected ClientRegistrationsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId)
        {
            var result = new List<ClientRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ClientRegistrationsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.catId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.issuedToId = (rdr[2] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.expiresAt = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.catType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[4];
            item.value = (rdr[5] == DBNull.Value) ? null : (string)rdr[5];
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        protected virtual async Task<List<ClientRegistrationsRecord>> GetAllCatsAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,catId,issuedToId,expiresAt,catType,value,created,modified FROM ClientRegistrations " +
                                             "WHERE identityId = @identityId "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<ClientRegistrationsRecord>();
                        }
                        var result = new List<ClientRegistrationsRecord>();
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

        protected ClientRegistrationsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Int32 catType)
        {
            var result = new List<ClientRegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new ClientRegistrationsRecord();
            item.identityId = identityId;
            item.catType = catType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.catId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.issuedToId = (rdr[2] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[2]);
            item.expiresAt = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.value = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.created = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.modified = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            return item;
       }

        protected virtual async Task<List<ClientRegistrationsRecord>> GetCatsByTypeAsync(Guid identityId,Int32 catType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,catId,issuedToId,expiresAt,value,created,modified FROM ClientRegistrations " +
                                             "WHERE identityId = @identityId AND catType = @catType "+
                                             ";";

                get2Command.AddParameter("@identityId", DbType.Binary, identityId);
                get2Command.AddParameter("@catType", DbType.Int32, catType);
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<ClientRegistrationsRecord>();
                        }
                        var result = new List<ClientRegistrationsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader2(rdr,identityId,catType));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<ClientRegistrationsRecord>, Guid? nextCursor)> PagingByIdentityIdAsync(int count, Guid identityId, Guid? inCursor)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = Guid.Empty;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging1Command = cn.CreateCommand();
            {
                getPaging1Command.CommandText = "SELECT rowId,identityId,catId,issuedToId,expiresAt,catType,value,created,modified FROM ClientRegistrations " +
                                            "WHERE (identityId = @identityId) AND identityId > @identityId  ORDER BY identityId ASC  LIMIT @count;";

                getPaging1Command.AddParameter("@identityId", DbType.Binary, inCursor?.ToByteArray());
                getPaging1Command.AddParameter("@count", DbType.Int64, count+1);
                getPaging1Command.AddParameter("@identityId", DbType.Binary, identityId);

                {
                    await using (var rdr = await getPaging1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ClientRegistrationsRecord>();
                        Guid? nextCursor;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].identityId;
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

        protected virtual async Task<(List<ClientRegistrationsRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,catId,issuedToId,expiresAt,catType,value,created,modified FROM ClientRegistrations " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<ClientRegistrationsRecord>();
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
