using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.Database.System.Connection;

#nullable disable

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.System.Table
{
    public record DkimKeysRecord
    {
        public Int64 rowId { get; set; }
        public OdinId domain { get; set; }
        public string selector { get; set; }
        public string algorithm { get; set; }
        public string publicKey { get; set; }
        public string privateKey { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            if (selector == null) throw new OdinDatabaseValidationException("Cannot be null selector");
            if (selector?.Length < 1) throw new OdinDatabaseValidationException($"Too short selector, was {selector.Length} (min 1)");
            if (selector?.Length > 63) throw new OdinDatabaseValidationException($"Too long selector, was {selector.Length} (max 63)");
            if (algorithm == null) throw new OdinDatabaseValidationException("Cannot be null algorithm");
            if (algorithm?.Length < 1) throw new OdinDatabaseValidationException($"Too short algorithm, was {algorithm.Length} (min 1)");
            if (algorithm?.Length > 32) throw new OdinDatabaseValidationException($"Too long algorithm, was {algorithm.Length} (max 32)");
            if (publicKey == null) throw new OdinDatabaseValidationException("Cannot be null publicKey");
            if (publicKey?.Length < 0) throw new OdinDatabaseValidationException($"Too short publicKey, was {publicKey.Length} (min 0)");
            if (publicKey?.Length > 65535) throw new OdinDatabaseValidationException($"Too long publicKey, was {publicKey.Length} (max 65535)");
            if (privateKey == null) throw new OdinDatabaseValidationException("Cannot be null privateKey");
            if (privateKey?.Length < 0) throw new OdinDatabaseValidationException($"Too short privateKey, was {privateKey.Length} (min 0)");
            if (privateKey?.Length > 65535) throw new OdinDatabaseValidationException($"Too long privateKey, was {privateKey.Length} (max 65535)");
        }
    } // End of record DkimKeysRecord

    public abstract class TableDkimKeysCRUD : TableBase
    {
        private readonly ScopedSystemConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "DkimKeys";

        public TableDkimKeysCRUD(ScopedSystemConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "DkimKeys");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE DkimKeys IS '{ \"Version\": 202608201100 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS DkimKeys( -- { \"Version\": 202608201100 }\n"
                   +rowid
                   +"domain TEXT NOT NULL, "
                   +"selector TEXT NOT NULL, "
                   +"algorithm TEXT NOT NULL, "
                   +"publicKey TEXT NOT NULL, "
                   +"privateKey TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +", UNIQUE(domain,selector)"
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "DkimKeys", createSql, commentSql);
        }
       */

        public virtual async Task<int> InsertAsync(DkimKeysRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DkimKeys (domain,selector,algorithm,publicKey,privateKey,created,modified) " +
                                           $"VALUES (@domain,@selector,@algorithm,@publicKey,@privateKey,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@domain", DbType.String, item.domain.DomainName);
                insertCommand.AddParameter("@selector", DbType.String, item.selector);
                insertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                insertCommand.AddParameter("@publicKey", DbType.String, item.publicKey);
                insertCommand.AddParameter("@privateKey", DbType.String, item.privateKey);
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

        public virtual async Task<bool> TryInsertAsync(DkimKeysRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO DkimKeys (domain,selector,algorithm,publicKey,privateKey,created,modified) " +
                                            $"VALUES (@domain,@selector,@algorithm,@publicKey,@privateKey,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@domain", DbType.String, item.domain.DomainName);
                insertCommand.AddParameter("@selector", DbType.String, item.selector);
                insertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                insertCommand.AddParameter("@publicKey", DbType.String, item.publicKey);
                insertCommand.AddParameter("@privateKey", DbType.String, item.privateKey);
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

        public virtual async Task<int> UpsertAsync(DkimKeysRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO DkimKeys (domain,selector,algorithm,publicKey,privateKey,created,modified) " +
                                            $"VALUES (@domain,@selector,@algorithm,@publicKey,@privateKey,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (domain,selector) DO UPDATE "+
                                            $"SET algorithm = @algorithm,publicKey = @publicKey,privateKey = @privateKey,modified = {upsertCommand.SqlMax()}(DkimKeys.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@domain", DbType.String, item.domain.DomainName);
                upsertCommand.AddParameter("@selector", DbType.String, item.selector);
                upsertCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                upsertCommand.AddParameter("@publicKey", DbType.String, item.publicKey);
                upsertCommand.AddParameter("@privateKey", DbType.String, item.privateKey);
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

        public virtual async Task<int> UpdateAsync(DkimKeysRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE DkimKeys " +
                                            $"SET algorithm = @algorithm,publicKey = @publicKey,privateKey = @privateKey,modified = {updateCommand.SqlMax()}(DkimKeys.modified+1,{sqlNowStr}) "+
                                            "WHERE (domain = @domain AND selector = @selector) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@domain", DbType.String, item.domain.DomainName);
                updateCommand.AddParameter("@selector", DbType.String, item.selector);
                updateCommand.AddParameter("@algorithm", DbType.String, item.algorithm);
                updateCommand.AddParameter("@publicKey", DbType.String, item.publicKey);
                updateCommand.AddParameter("@privateKey", DbType.String, item.privateKey);
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

        public new async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DkimKeys;";
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
            sl.Add("domain");
            sl.Add("selector");
            sl.Add("algorithm");
            sl.Add("publicKey");
            sl.Add("privateKey");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,domain,selector,algorithm,publicKey,privateKey,created,modified
        public DkimKeysRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DkimKeysRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DkimKeysRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.domain = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.selector = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.algorithm = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.publicKey = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.privateKey = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.created = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[6]);
            item.modified = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(OdinId domain,string selector)
        {
            if (selector == null) throw new OdinDatabaseValidationException("Cannot be null selector");
            if (selector?.Length < 1) throw new OdinDatabaseValidationException($"Too short selector, was {selector.Length} (min 1)");
            if (selector?.Length > 63) throw new OdinDatabaseValidationException($"Too long selector, was {selector.Length} (max 63)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DkimKeys " +
                                             "WHERE domain = @domain AND selector = @selector";

                delete0Command.AddParameter("@domain", DbType.String, domain.DomainName);
                delete0Command.AddParameter("@selector", DbType.String, selector);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<DkimKeysRecord> PopAsync(OdinId domain,string selector)
        {
            if (selector == null) throw new OdinDatabaseValidationException("Cannot be null selector");
            if (selector?.Length < 1) throw new OdinDatabaseValidationException($"Too short selector, was {selector.Length} (min 1)");
            if (selector?.Length > 63) throw new OdinDatabaseValidationException($"Too long selector, was {selector.Length} (max 63)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM DkimKeys " +
                                             "WHERE domain = @domain AND selector = @selector " + 
                                             "RETURNING rowId,algorithm,publicKey,privateKey,created,modified";

                deleteCommand.AddParameter("@domain", DbType.String, domain.DomainName);
                deleteCommand.AddParameter("@selector", DbType.String, selector);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,domain,selector);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public DkimKeysRecord ReadRecordFromReader0(DbDataReader rdr,OdinId domain,string selector)
        {
            if (selector == null) throw new OdinDatabaseValidationException("Cannot be null selector");
            if (selector?.Length < 1) throw new OdinDatabaseValidationException($"Too short selector, was {selector.Length} (min 1)");
            if (selector?.Length > 63) throw new OdinDatabaseValidationException($"Too long selector, was {selector.Length} (max 63)");
            var result = new List<DkimKeysRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DkimKeysRecord();
            item.domain = domain;
            item.selector = selector;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.algorithm = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.publicKey = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.privateKey = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.created = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.modified = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            return item;
       }

        public virtual async Task<DkimKeysRecord> GetAsync(OdinId domain,string selector)
        {
            if (selector == null) throw new OdinDatabaseValidationException("Cannot be null selector");
            if (selector?.Length < 1) throw new OdinDatabaseValidationException($"Too short selector, was {selector.Length} (min 1)");
            if (selector?.Length > 63) throw new OdinDatabaseValidationException($"Too long selector, was {selector.Length} (max 63)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,algorithm,publicKey,privateKey,created,modified FROM DkimKeys " +
                                             "WHERE domain = @domain AND selector = @selector LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@domain", DbType.String, domain.DomainName);
                get0Command.AddParameter("@selector", DbType.String, selector);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,domain,selector);
                        return r;
                    } // using
                } //
            } // using
        }

        public virtual async Task<(List<DkimKeysRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,domain,selector,algorithm,publicKey,privateKey,created,modified FROM DkimKeys " +
                                            "WHERE rowId > @rowId  ORDER BY rowId ASC  LIMIT @count;";

                getPaging0Command.AddParameter("@rowId", DbType.Int64, inCursor);
                getPaging0Command.AddParameter("@count", DbType.Int64, count+1);

                {
                    await using (var rdr = await getPaging0Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DkimKeysRecord>();
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
