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
    public record KeyTwoValueRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public byte[] key1 { get; set; }
        public byte[] key2 { get; set; }
        public byte[] data { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 128) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 128)");
            if (data?.Length < 0) throw new OdinDatabaseValidationException($"Too short data, was {data.Length} (min 0)");
            if (data?.Length > 1048576) throw new OdinDatabaseValidationException($"Too long data, was {data.Length} (max 1048576)");
        }
    } // End of record KeyTwoValueRecord

    public abstract class TableKeyTwoValueCRUD : TableBase
    {
        private ScopedIdentityConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "KeyTwoValue";

        protected TableKeyTwoValueCRUD(ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public override async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await SqlHelper.DeleteTableAsync(cn, "KeyTwoValue");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE KeyTwoValue IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS KeyTwoValue( -- { \"Version\": 0 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"key1 BYTEA NOT NULL, "
                   +"key2 BYTEA , "
                   +"data BYTEA  "
                   +", UNIQUE(identityId,key1)"
                   +$"){wori};"
                   +"CREATE INDEX IF NOT EXISTS Idx0KeyTwoValue ON KeyTwoValue(identityId,key2);"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "KeyTwoValue", createSql, commentSql);
        }

        protected virtual async Task<int> InsertAsync(KeyTwoValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyTwoValue (identityId,key1,key2,data) " +
                                           $"VALUES (@identityId,@key1,@key2,@data)"+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@key1", DbType.Binary, item.key1);
                insertCommand.AddParameter("@key2", DbType.Binary, item.key2);
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

        protected virtual async Task<bool> TryInsertAsync(KeyTwoValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                insertCommand.CommandText = "INSERT INTO KeyTwoValue (identityId,key1,key2,data) " +
                                            $"VALUES (@identityId,@key1,@key2,@data) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING -1,-1,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@key1", DbType.Binary, item.key1);
                insertCommand.AddParameter("@key2", DbType.Binary, item.key2);
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

        protected virtual async Task<int> UpsertAsync(KeyTwoValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                upsertCommand.CommandText = "INSERT INTO KeyTwoValue (identityId,key1,key2,data) " +
                                            $"VALUES (@identityId,@key1,@key2,@data)"+
                                            "ON CONFLICT (identityId,key1) DO UPDATE "+
                                            $"SET key2 = @key2,data = @data "+
                                            "RETURNING -1,-1,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@key1", DbType.Binary, item.key1);
                upsertCommand.AddParameter("@key2", DbType.Binary, item.key2);
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

        protected virtual async Task<int> UpdateAsync(KeyTwoValueRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                updateCommand.CommandText = "UPDATE KeyTwoValue " +
                                            $"SET key2 = @key2,data = @data "+
                                            "WHERE (identityId = @identityId AND key1 = @key1) "+
                                            "RETURNING -1,-1,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@key1", DbType.Binary, item.key1);
                updateCommand.AddParameter("@key2", DbType.Binary, item.key2);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM KeyTwoValue;";
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
            sl.Add("key1");
            sl.Add("key2");
            sl.Add("data");
            return sl;
        }

        // SELECT rowId,identityId,key1,key2,data
        protected KeyTwoValueRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<KeyTwoValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.key1 = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[2]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.key2 = (rdr[3] == DBNull.Value) ? null : (byte[])(rdr[3]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.data = (rdr[4] == DBNull.Value) ? null : (byte[])(rdr[4]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM KeyTwoValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                delete0Command.AddParameter("@key1", DbType.Binary, key1);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected virtual async Task<KeyTwoValueRecord> PopAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM KeyTwoValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 " + 
                                             "RETURNING rowId,key2,data";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                deleteCommand.AddParameter("@key1", DbType.Binary, key1);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId,key1);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        protected KeyTwoValueRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            var result = new List<KeyTwoValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.identityId = identityId;
            item.key1 = key1;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.key2 = (rdr[1] == DBNull.Value) ? null : (byte[])(rdr[1]);
            if (item.key2?.Length < 0)
                throw new Exception("Too little data in key2...");
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<KeyTwoValueRecord> GetAsync(Guid identityId,byte[] key1)
        {
            if (key1 == null) throw new OdinDatabaseValidationException("Cannot be null key1");
            if (key1?.Length < 16) throw new OdinDatabaseValidationException($"Too short key1, was {key1.Length} (min 16)");
            if (key1?.Length > 48) throw new OdinDatabaseValidationException($"Too long key1, was {key1.Length} (max 48)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,key2,data FROM KeyTwoValue " +
                                             "WHERE identityId = @identityId AND key1 = @key1 LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                get0Command.AddParameter("@key1", DbType.Binary, key1);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,key1);
                        return r;
                    } // using
                } //
            } // using
        }

        protected KeyTwoValueRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,byte[] key2)
        {
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 128) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 128)");
            var result = new List<KeyTwoValueRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new KeyTwoValueRecord();
            item.identityId = identityId;
            item.key2 = key2;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.key1 = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.key1?.Length < 16)
                throw new Exception("Too little data in key1...");
            item.data = (rdr[2] == DBNull.Value) ? null : (byte[])(rdr[2]);
            if (item.data?.Length < 0)
                throw new Exception("Too little data in data...");
            return item;
       }

        protected virtual async Task<List<KeyTwoValueRecord>> GetByKeyTwoAsync(Guid identityId,byte[] key2)
        {
            if (key2?.Length < 0) throw new OdinDatabaseValidationException($"Too short key2, was {key2.Length} (min 0)");
            if (key2?.Length > 128) throw new OdinDatabaseValidationException($"Too long key2, was {key2.Length} (max 128)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,key1,data FROM KeyTwoValue " +
                                             "WHERE identityId = @identityId AND "+(key2==null ? "key2 IS NULL" : "key2 = @key2") + " "+
                                             ";";

                get1Command.AddParameter("@identityId", DbType.Binary, identityId);
                get1Command.AddParameter("@key2", DbType.Binary, key2);
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<KeyTwoValueRecord>();
                        }
                        var result = new List<KeyTwoValueRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,key2));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<KeyTwoValueRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,identityId,key1,key2,data FROM KeyTwoValue " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<KeyTwoValueRecord>();
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
