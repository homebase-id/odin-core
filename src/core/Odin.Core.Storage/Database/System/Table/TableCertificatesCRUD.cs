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

namespace Odin.Core.Storage.Database.System.Table
{
    public record CertificatesRecord
    {
        public Int64 rowId { get; set; }
        public OdinId domain { get; set; }
        public string privateKey { get; set; }
        public string certificate { get; set; }
        public UnixTimeUtc expiration { get; set; }
        public UnixTimeUtc lastAttempt { get; set; }
        public string correlationId { get; set; }
        public string lastError { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            if (privateKey == null) throw new OdinDatabaseValidationException("Cannot be null privateKey");
            if (privateKey?.Length < 0) throw new OdinDatabaseValidationException($"Too short privateKey, was {privateKey.Length} (min 0)");
            if (privateKey?.Length > 65535) throw new OdinDatabaseValidationException($"Too long privateKey, was {privateKey.Length} (max 65535)");
            if (certificate == null) throw new OdinDatabaseValidationException("Cannot be null certificate");
            if (certificate?.Length < 0) throw new OdinDatabaseValidationException($"Too short certificate, was {certificate.Length} (min 0)");
            if (certificate?.Length > 65535) throw new OdinDatabaseValidationException($"Too long certificate, was {certificate.Length} (max 65535)");
            if (correlationId == null) throw new OdinDatabaseValidationException("Cannot be null correlationId");
            if (correlationId?.Length < 0) throw new OdinDatabaseValidationException($"Too short correlationId, was {correlationId.Length} (min 0)");
            if (correlationId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long correlationId, was {correlationId.Length} (max 65535)");
            if (lastError?.Length < 0) throw new OdinDatabaseValidationException($"Too short lastError, was {lastError.Length} (min 0)");
            if (lastError?.Length > 65535) throw new OdinDatabaseValidationException($"Too long lastError, was {lastError.Length} (max 65535)");
        }
    } // End of record CertificatesRecord

    public abstract class TableCertificatesCRUD
    {
        private readonly ScopedSystemConnectionFactory _scopedConnectionFactory;

        public TableCertificatesCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "Certificates");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Certificates IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE Certificates( -- { \"Version\": 0 }\n"
                   +rowid
                   +"domain TEXT NOT NULL UNIQUE, "
                   +"privateKey TEXT NOT NULL, "
                   +"certificate TEXT NOT NULL, "
                   +"expiration BIGINT NOT NULL, "
                   +"lastAttempt BIGINT NOT NULL, "
                   +"correlationId TEXT NOT NULL, "
                   +"lastError TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await MigrationBase.CreateTableIfNotExistsAsync(cn, createSql, commentSql);
        }

        public virtual async Task<int> InsertAsync(CertificatesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Certificates (domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified) " +
                                           $"VALUES (@domain,@privateKey,@certificate,@expiration,@lastAttempt,@correlationId,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.String;
                insertParam1.ParameterName = "@domain";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@privateKey";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@certificate";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int64;
                insertParam4.ParameterName = "@expiration";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@lastAttempt";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@lastError";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.domain.DomainName;
                insertParam2.Value = item.privateKey;
                insertParam3.Value = item.certificate;
                insertParam4.Value = item.expiration.milliseconds;
                insertParam5.Value = item.lastAttempt.milliseconds;
                insertParam6.Value = item.correlationId;
                insertParam7.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<bool> TryInsertAsync(CertificatesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Certificates (domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified) " +
                                            $"VALUES (@domain,@privateKey,@certificate,@expiration,@lastAttempt,@correlationId,@lastError,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.String;
                insertParam1.ParameterName = "@domain";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@privateKey";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@certificate";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.Int64;
                insertParam4.ParameterName = "@expiration";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Int64;
                insertParam5.ParameterName = "@lastAttempt";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.String;
                insertParam6.ParameterName = "@correlationId";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@lastError";
                insertCommand.Parameters.Add(insertParam7);
                insertParam1.Value = item.domain.DomainName;
                insertParam2.Value = item.privateKey;
                insertParam3.Value = item.certificate;
                insertParam4.Value = item.expiration.milliseconds;
                insertParam5.Value = item.lastAttempt.milliseconds;
                insertParam6.Value = item.correlationId;
                insertParam7.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<int> UpsertAsync(CertificatesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Certificates (domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified) " +
                                            $"VALUES (@domain,@privateKey,@certificate,@expiration,@lastAttempt,@correlationId,@lastError,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (domain) DO UPDATE "+
                                            $"SET privateKey = @privateKey,certificate = @certificate,expiration = @expiration,lastAttempt = @lastAttempt,correlationId = @correlationId,lastError = @lastError,modified = {upsertCommand.SqlMax()}(Certificates.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.String;
                upsertParam1.ParameterName = "@domain";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.String;
                upsertParam2.ParameterName = "@privateKey";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.String;
                upsertParam3.ParameterName = "@certificate";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.Int64;
                upsertParam4.ParameterName = "@expiration";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Int64;
                upsertParam5.ParameterName = "@lastAttempt";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.String;
                upsertParam6.ParameterName = "@correlationId";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.String;
                upsertParam7.ParameterName = "@lastError";
                upsertCommand.Parameters.Add(upsertParam7);
                upsertParam1.Value = item.domain.DomainName;
                upsertParam2.Value = item.privateKey;
                upsertParam3.Value = item.certificate;
                upsertParam4.Value = item.expiration.milliseconds;
                upsertParam5.Value = item.lastAttempt.milliseconds;
                upsertParam6.Value = item.correlationId;
                upsertParam7.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<int> UpdateAsync(CertificatesRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Certificates " +
                                            $"SET privateKey = @privateKey,certificate = @certificate,expiration = @expiration,lastAttempt = @lastAttempt,correlationId = @correlationId,lastError = @lastError,modified = {updateCommand.SqlMax()}(Certificates.modified+1,{sqlNowStr}) "+
                                            "WHERE (domain = @domain) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.String;
                updateParam1.ParameterName = "@domain";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.String;
                updateParam2.ParameterName = "@privateKey";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.String;
                updateParam3.ParameterName = "@certificate";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.Int64;
                updateParam4.ParameterName = "@expiration";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Int64;
                updateParam5.ParameterName = "@lastAttempt";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.String;
                updateParam6.ParameterName = "@correlationId";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.String;
                updateParam7.ParameterName = "@lastError";
                updateCommand.Parameters.Add(updateParam7);
                updateParam1.Value = item.domain.DomainName;
                updateParam2.Value = item.privateKey;
                updateParam3.Value = item.certificate;
                updateParam4.Value = item.expiration.milliseconds;
                updateParam5.Value = item.lastAttempt.milliseconds;
                updateParam6.Value = item.correlationId;
                updateParam7.Value = item.lastError ?? (object)DBNull.Value;
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

        public virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Certificates;";
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
            sl.Add("domain");
            sl.Add("privateKey");
            sl.Add("certificate");
            sl.Add("expiration");
            sl.Add("lastAttempt");
            sl.Add("correlationId");
            sl.Add("lastError");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified
        public CertificatesRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<CertificatesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CertificatesRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.domain = (rdr[1] == DBNull.Value) ?                 throw new Exception("item is NULL, but set as NOT NULL") : new OdinId((string)rdr[1]);
            item.privateKey = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.certificate = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.expiration = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.lastAttempt = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[5]);
            item.correlationId = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.lastError = (rdr[7] == DBNull.Value) ? null : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        public virtual async Task<int> DeleteAsync(OdinId domain)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Certificates " +
                                             "WHERE domain = @domain";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.String;
                delete0Param1.ParameterName = "@domain";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = domain.DomainName;
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<CertificatesRecord> PopAsync(OdinId domain)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Certificates " +
                                             "WHERE domain = @domain " + 
                                             "RETURNING rowId,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified";
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.String;
                deleteParam1.ParameterName = "@domain";
                deleteCommand.Parameters.Add(deleteParam1);

                deleteParam1.Value = domain.DomainName;
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,domain);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public CertificatesRecord ReadRecordFromReader0(DbDataReader rdr,OdinId domain)
        {
            var result = new List<CertificatesRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new CertificatesRecord();
            item.domain = domain;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.privateKey = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[1];
            item.certificate = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.expiration = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.lastAttempt = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            item.correlationId = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.lastError = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[8]); // HACK
            return item;
       }

        public virtual async Task<CertificatesRecord> GetAsync(OdinId domain)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified FROM Certificates " +
                                             "WHERE domain = @domain LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.String;
                get0Param1.ParameterName = "@domain";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = domain.DomainName;
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,domain);
                        return r;
                    } // using
                } //
            } // using
        }

        public virtual async Task<(List<CertificatesRecord>, Int64? nextCursor)> PagingByRowIdAsync(int count, Int64? inCursor)
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
                getPaging0Command.CommandText = "SELECT rowId,domain,privateKey,certificate,expiration,lastAttempt,correlationId,lastError,created,modified FROM Certificates " +
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
                        var result = new List<CertificatesRecord>();
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
