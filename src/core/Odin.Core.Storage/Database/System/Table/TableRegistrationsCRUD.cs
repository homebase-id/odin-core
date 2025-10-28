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

namespace Odin.Core.Storage.Database.System.Table
{
    public record RegistrationsRecord
    {
        public Int64 rowId { get; set; }
        public Guid identityId { get; set; }
        public string email { get; set; }
        public string primaryDomainName { get; set; }
        public string firstRunToken { get; set; }
        public Boolean disabled { get; set; }
        public UnixTimeUtc? markedForDeletionDate { get; set; }
        public string planId { get; set; }
        public string json { get; set; }
        public UnixTimeUtc created { get; set; }
        public UnixTimeUtc modified { get; set; }
        public void Validate()
        {
            identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            if (email?.Length < 0) throw new OdinDatabaseValidationException($"Too short email, was {email.Length} (min 0)");
            if (email?.Length > 65535) throw new OdinDatabaseValidationException($"Too long email, was {email.Length} (max 65535)");
            if (primaryDomainName == null) throw new OdinDatabaseValidationException("Cannot be null primaryDomainName");
            if (primaryDomainName?.Length < 0) throw new OdinDatabaseValidationException($"Too short primaryDomainName, was {primaryDomainName.Length} (min 0)");
            if (primaryDomainName?.Length > 65535) throw new OdinDatabaseValidationException($"Too long primaryDomainName, was {primaryDomainName.Length} (max 65535)");
            if (firstRunToken?.Length < 0) throw new OdinDatabaseValidationException($"Too short firstRunToken, was {firstRunToken.Length} (min 0)");
            if (firstRunToken?.Length > 65535) throw new OdinDatabaseValidationException($"Too long firstRunToken, was {firstRunToken.Length} (max 65535)");
            if (planId?.Length < 0) throw new OdinDatabaseValidationException($"Too short planId, was {planId.Length} (min 0)");
            if (planId?.Length > 65535) throw new OdinDatabaseValidationException($"Too long planId, was {planId.Length} (max 65535)");
            if (json?.Length < 0) throw new OdinDatabaseValidationException($"Too short json, was {json.Length} (min 0)");
            if (json?.Length > 65535) throw new OdinDatabaseValidationException($"Too long json, was {json.Length} (max 65535)");
        }
    } // End of record RegistrationsRecord

    public abstract class TableRegistrationsCRUD : TableBase
    {
        private ScopedSystemConnectionFactory _scopedConnectionFactory { get; init; }
        public override string TableName { get; } = "Registrations";

        public TableRegistrationsCRUD(ScopedSystemConnectionFactory scopedConnectionFactory)
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
                await SqlHelper.DeleteTableAsync(cn, "Registrations");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowId BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Registrations IS '{ \"Version\": 202509090509 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE IF NOT EXISTS Registrations( -- { \"Version\": 202509090509 }\n"
                   +rowid
                   +"identityId BYTEA NOT NULL UNIQUE, "
                   +"email TEXT , "
                   +"primaryDomainName TEXT NOT NULL UNIQUE, "
                   +"firstRunToken TEXT , "
                   +"disabled BOOLEAN NOT NULL, "
                   +"markedForDeletionDate BIGINT , "
                   +"planId TEXT , "
                   +"json TEXT , "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT NOT NULL "
                   +$"){wori};"
                   ;
            await SqlHelper.CreateTableWithCommentAsync(cn, "Registrations", createSql, commentSql);
        }
       */

        public virtual async Task<int> InsertAsync(RegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Registrations (identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified) " +
                                           $"VALUES (@identityId,@email,@primaryDomainName,@firstRunToken,@disabled,@markedForDeletionDate,@planId,@json,{sqlNowStr},{sqlNowStr})"+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@email", DbType.String, item.email);
                insertCommand.AddParameter("@primaryDomainName", DbType.String, item.primaryDomainName);
                insertCommand.AddParameter("@firstRunToken", DbType.String, item.firstRunToken);
                insertCommand.AddParameter("@disabled", DbType.Boolean, item.disabled);
                insertCommand.AddParameter("@markedForDeletionDate", DbType.Int64, item.markedForDeletionDate?.milliseconds);
                insertCommand.AddParameter("@planId", DbType.String, item.planId);
                insertCommand.AddParameter("@json", DbType.String, item.json);
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

        public virtual async Task<bool> TryInsertAsync(RegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr = insertCommand.SqlNow();
                insertCommand.CommandText = "INSERT INTO Registrations (identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified) " +
                                            $"VALUES (@identityId,@email,@primaryDomainName,@firstRunToken,@disabled,@markedForDeletionDate,@planId,@json,{sqlNowStr},{sqlNowStr}) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                insertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                insertCommand.AddParameter("@email", DbType.String, item.email);
                insertCommand.AddParameter("@primaryDomainName", DbType.String, item.primaryDomainName);
                insertCommand.AddParameter("@firstRunToken", DbType.String, item.firstRunToken);
                insertCommand.AddParameter("@disabled", DbType.Boolean, item.disabled);
                insertCommand.AddParameter("@markedForDeletionDate", DbType.Int64, item.markedForDeletionDate?.milliseconds);
                insertCommand.AddParameter("@planId", DbType.String, item.planId);
                insertCommand.AddParameter("@json", DbType.String, item.json);
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

        public virtual async Task<int> UpsertAsync(RegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr = upsertCommand.SqlNow();
                upsertCommand.CommandText = "INSERT INTO Registrations (identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified) " +
                                            $"VALUES (@identityId,@email,@primaryDomainName,@firstRunToken,@disabled,@markedForDeletionDate,@planId,@json,{sqlNowStr},{sqlNowStr})"+
                                            "ON CONFLICT (identityId) DO UPDATE "+
                                            $"SET email = @email,primaryDomainName = @primaryDomainName,firstRunToken = @firstRunToken,disabled = @disabled,markedForDeletionDate = @markedForDeletionDate,planId = @planId,json = @json,modified = {upsertCommand.SqlMax()}(Registrations.modified+1,{sqlNowStr}) "+
                                            "RETURNING created,modified,rowId;";
                upsertCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                upsertCommand.AddParameter("@email", DbType.String, item.email);
                upsertCommand.AddParameter("@primaryDomainName", DbType.String, item.primaryDomainName);
                upsertCommand.AddParameter("@firstRunToken", DbType.String, item.firstRunToken);
                upsertCommand.AddParameter("@disabled", DbType.Boolean, item.disabled);
                upsertCommand.AddParameter("@markedForDeletionDate", DbType.Int64, item.markedForDeletionDate?.milliseconds);
                upsertCommand.AddParameter("@planId", DbType.String, item.planId);
                upsertCommand.AddParameter("@json", DbType.String, item.json);
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

        public virtual async Task<int> UpdateAsync(RegistrationsRecord item)
        {
            item.Validate();
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr = updateCommand.SqlNow();
                updateCommand.CommandText = "UPDATE Registrations " +
                                            $"SET email = @email,primaryDomainName = @primaryDomainName,firstRunToken = @firstRunToken,disabled = @disabled,markedForDeletionDate = @markedForDeletionDate,planId = @planId,json = @json,modified = {updateCommand.SqlMax()}(Registrations.modified+1,{sqlNowStr}) "+
                                            "WHERE (identityId = @identityId) "+
                                            "RETURNING created,modified,rowId;";
                updateCommand.AddParameter("@identityId", DbType.Binary, item.identityId);
                updateCommand.AddParameter("@email", DbType.String, item.email);
                updateCommand.AddParameter("@primaryDomainName", DbType.String, item.primaryDomainName);
                updateCommand.AddParameter("@firstRunToken", DbType.String, item.firstRunToken);
                updateCommand.AddParameter("@disabled", DbType.Boolean, item.disabled);
                updateCommand.AddParameter("@markedForDeletionDate", DbType.Int64, item.markedForDeletionDate?.milliseconds);
                updateCommand.AddParameter("@planId", DbType.String, item.planId);
                updateCommand.AddParameter("@json", DbType.String, item.json);
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Registrations;";
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
            sl.Add("email");
            sl.Add("primaryDomainName");
            sl.Add("firstRunToken");
            sl.Add("disabled");
            sl.Add("markedForDeletionDate");
            sl.Add("planId");
            sl.Add("json");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified
        public RegistrationsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<RegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new RegistrationsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.email = (rdr[2] == DBNull.Value) ? null : (string)rdr[2];
            item.primaryDomainName = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.firstRunToken = (rdr[4] == DBNull.Value) ? null : (string)rdr[4];
            item.disabled = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[5]);
            item.markedForDeletionDate = (rdr[6] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[6]);
            item.planId = (rdr[7] == DBNull.Value) ? null : (string)rdr[7];
            item.json = (rdr[8] == DBNull.Value) ? null : (string)rdr[8];
            item.created = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            item.modified = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Registrations " +
                                             "WHERE identityId = @identityId";

                delete0Command.AddParameter("@identityId", DbType.Binary, identityId);
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        public virtual async Task<RegistrationsRecord> PopAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = "DELETE FROM Registrations " +
                                             "WHERE identityId = @identityId " + 
                                             "RETURNING rowId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified";

                deleteCommand.AddParameter("@identityId", DbType.Binary, identityId);
                using (var rdr = await deleteCommand.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await rdr.ReadAsync())
                    {
                       return ReadRecordFromReader0(rdr,identityId);
                    }
                    else
                    {
                       return null;
                    }
                }
            }
        }

        public RegistrationsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId)
        {
            var result = new List<RegistrationsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new RegistrationsRecord();
            item.identityId = identityId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.email = (rdr[1] == DBNull.Value) ? null : (string)rdr[1];
            item.primaryDomainName = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.firstRunToken = (rdr[3] == DBNull.Value) ? null : (string)rdr[3];
            item.disabled = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : Convert.ToBoolean(rdr[4]);
            item.markedForDeletionDate = (rdr[5] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[5]);
            item.planId = (rdr[6] == DBNull.Value) ? null : (string)rdr[6];
            item.json = (rdr[7] == DBNull.Value) ? null : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        public virtual async Task<RegistrationsRecord> GetAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified FROM Registrations " +
                                             "WHERE identityId = @identityId LIMIT 1 "+
                                             ";";

                get0Command.AddParameter("@identityId", DbType.Binary, identityId);
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId);
                        return r;
                    } // using
                } //
            } // using
        }

    }
}
