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

namespace Odin.Core.Storage.Database.Attestation.Table
{
    public record AttestationStatusRecord
    {
        public Int64 rowId { get; set; }
        public byte[] attestationId { get; set; }
        public Int32 status { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
        }
    } // End of record AttestationStatusRecord

    public abstract class TableAttestationStatusCRUD : TableBase
    {
        private readonly ScopedAttestationConnectionFactory _scopedConnectionFactory;
        public override string TableName { get; } = "AttestationStatus";

        public TableAttestationStatusCRUD(ScopedAttestationConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "AttestationStatus");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE AttestationStatus IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS AttestationStatus( -- { \"Version\": 0 }\n"
                   +rowid
                   +"attestationId BYTEA NOT NULL UNIQUE, "
                   +"status BIGINT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "AttestationStatus", createSql, commentSql);
        }
       */

        public virtual async Task<int> InsertAsync(AttestationStatusRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                           $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@attestationId", DbType.Binary, item.attestationId);
                insertCommand.AddParameter("@status", DbType.Int32, item.status);
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

        public virtual async Task<bool> TryInsertAsync(AttestationStatusRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                            $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@attestationId", DbType.Binary, item.attestationId);
                insertCommand.AddParameter("@status", DbType.Int32, item.status);
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

        public virtual async Task<int> UpsertAsync(AttestationStatusRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO AttestationStatus (attestationId,status,created,modified) " +
                                            $"VALUES (@attestationId,@status,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (attestationId) DO UPDATE "+
                                            $"SET status = @status,modified = {upsertCommand.SqlMax()}(AttestationStatus.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@attestationId", DbType.Binary, item.attestationId);
                upsertCommand.AddParameter("@status", DbType.Int32, item.status);
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

        public virtual async Task<int> UpdateAsync(AttestationStatusRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE AttestationStatus " +
                                            $"SET status = @status,modified = {updateCommand.SqlMax()}(AttestationStatus.modified+1,{sqlNowStr}) "+
                                            "WHERE (attestationId = @attestationId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@attestationId", DbType.Binary, item.attestationId);
                updateCommand.AddParameter("@status", DbType.Int32, item.status);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM AttestationStatus;";
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
            sl.Add("attestationId");
            sl.Add("status");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,attestationId,status,created,modified
        public AttestationStatusRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<AttestationStatusRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.attestationId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (byte[])(rdr[1]);
            if (item.attestationId?.Length < 16)
                throw new Exception("Too little data in attestationId...");
            item.status = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[2];
            item.created = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            item.modified = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[4]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM AttestationStatus " +
                                             "WHERE attestationId = @attestationId";

                delete0Command.AddParameter("@attestationId", DbType.Binary, attestationId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<AttestationStatusRecord> PopAsync(byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM AttestationStatus " +
                                             "WHERE attestationId = @attestationId " + 
                                             "RETURNING rowId,status,created,modified";

                deleteCommand.AddParameter("@attestationId", DbType.Binary, attestationId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,attestationId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public AttestationStatusRecord ReadRecordFromReader0(DbDataReader rdr,byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            var result = new List<AttestationStatusRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new AttestationStatusRecord();
            item.attestationId = attestationId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.status = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (int)(long)rdr[1];
            item.created = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[2]);
            item.modified = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[3]);
            return item;
       }

        public virtual async Task<AttestationStatusRecord> GetAsync(byte[] attestationId)
        {
            if (attestationId == null) throw new OdinDatabaseValidationException("Cannot be null attestationId");
            if (attestationId?.Length < 16) throw new OdinDatabaseValidationException($"Too short attestationId, was {attestationId.Length} (min 16)");
            if (attestationId?.Length > 64) throw new OdinDatabaseValidationException($"Too long attestationId, was {attestationId.Length} (max 64)");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,status,created,modified FROM AttestationStatus " +
                                             "WHERE attestationId = @attestationId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@attestationId", DbType.Binary, attestationId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,attestationId);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
