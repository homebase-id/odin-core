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

    public abstract class TableRegistrationsCRUD
    {
        private readonly ScopedSystemConnectionFactory _scopedConnectionFactory;

        public TableRegistrationsCRUD(CacheHelper cache, ScopedSystemConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            if (dropExisting)
                await MigrationBase.DeleteTableAsync(cn, "Registrations");
            var rowid = "";
            var commentSql = "";
            if (cn.DatabaseType == DatabaseType.Postgres)
            {
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
               commentSql = "COMMENT ON TABLE Registrations IS '{ \"Version\": 0 }';";
            }
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            string createSql =
                "CREATE TABLE Registrations( -- { \"Version\": 0 }\n"
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
            await MigrationBase.CreateTableAsync(cn, createSql, commentSql);
        }

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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@email";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@primaryDomainName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@firstRunToken";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Boolean;
                insertParam5.ParameterName = "@disabled";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int64;
                insertParam6.ParameterName = "@markedForDeletionDate";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@planId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@json";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.email ?? (object)DBNull.Value;
                insertParam3.Value = item.primaryDomainName;
                insertParam4.Value = item.firstRunToken ?? (object)DBNull.Value;
                insertParam5.Value = item.disabled;
                insertParam6.Value = item.markedForDeletionDate == null ? (object)DBNull.Value : item.markedForDeletionDate?.milliseconds;
                insertParam7.Value = item.planId ?? (object)DBNull.Value;
                insertParam8.Value = item.json ?? (object)DBNull.Value;
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
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.DbType = DbType.Binary;
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.DbType = DbType.String;
                insertParam2.ParameterName = "@email";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.DbType = DbType.String;
                insertParam3.ParameterName = "@primaryDomainName";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.DbType = DbType.String;
                insertParam4.ParameterName = "@firstRunToken";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.DbType = DbType.Boolean;
                insertParam5.ParameterName = "@disabled";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.DbType = DbType.Int64;
                insertParam6.ParameterName = "@markedForDeletionDate";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.DbType = DbType.String;
                insertParam7.ParameterName = "@planId";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.DbType = DbType.String;
                insertParam8.ParameterName = "@json";
                insertCommand.Parameters.Add(insertParam8);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.email ?? (object)DBNull.Value;
                insertParam3.Value = item.primaryDomainName;
                insertParam4.Value = item.firstRunToken ?? (object)DBNull.Value;
                insertParam5.Value = item.disabled;
                insertParam6.Value = item.markedForDeletionDate == null ? (object)DBNull.Value : item.markedForDeletionDate?.milliseconds;
                insertParam7.Value = item.planId ?? (object)DBNull.Value;
                insertParam8.Value = item.json ?? (object)DBNull.Value;
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
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.DbType = DbType.Binary;
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.DbType = DbType.String;
                upsertParam2.ParameterName = "@email";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.DbType = DbType.String;
                upsertParam3.ParameterName = "@primaryDomainName";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.DbType = DbType.String;
                upsertParam4.ParameterName = "@firstRunToken";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.DbType = DbType.Boolean;
                upsertParam5.ParameterName = "@disabled";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.DbType = DbType.Int64;
                upsertParam6.ParameterName = "@markedForDeletionDate";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.DbType = DbType.String;
                upsertParam7.ParameterName = "@planId";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.DbType = DbType.String;
                upsertParam8.ParameterName = "@json";
                upsertCommand.Parameters.Add(upsertParam8);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.email ?? (object)DBNull.Value;
                upsertParam3.Value = item.primaryDomainName;
                upsertParam4.Value = item.firstRunToken ?? (object)DBNull.Value;
                upsertParam5.Value = item.disabled;
                upsertParam6.Value = item.markedForDeletionDate == null ? (object)DBNull.Value : item.markedForDeletionDate?.milliseconds;
                upsertParam7.Value = item.planId ?? (object)DBNull.Value;
                upsertParam8.Value = item.json ?? (object)DBNull.Value;
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
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.DbType = DbType.Binary;
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.DbType = DbType.String;
                updateParam2.ParameterName = "@email";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.DbType = DbType.String;
                updateParam3.ParameterName = "@primaryDomainName";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.DbType = DbType.String;
                updateParam4.ParameterName = "@firstRunToken";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.DbType = DbType.Boolean;
                updateParam5.ParameterName = "@disabled";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.DbType = DbType.Int64;
                updateParam6.ParameterName = "@markedForDeletionDate";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.DbType = DbType.String;
                updateParam7.ParameterName = "@planId";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.DbType = DbType.String;
                updateParam8.ParameterName = "@json";
                updateCommand.Parameters.Add(updateParam8);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.email ?? (object)DBNull.Value;
                updateParam3.Value = item.primaryDomainName;
                updateParam4.Value = item.firstRunToken ?? (object)DBNull.Value;
                updateParam5.Value = item.disabled;
                updateParam6.Value = item.markedForDeletionDate == null ? (object)DBNull.Value : item.markedForDeletionDate?.milliseconds;
                updateParam7.Value = item.planId ?? (object)DBNull.Value;
                updateParam8.Value = item.json ?? (object)DBNull.Value;
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
                getCountCommand.CommandText = "SELECT COUNT(*) FROM Registrations;";
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
            item.modified = (rdr[10] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[10]); // HACK
            return item;
       }

        public virtual async Task<int> DeleteAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM Registrations " +
                                             "WHERE identityId = @identityId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.DbType = DbType.Binary;
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);

                delete0Param1.Value = identityId.ToByteArray();
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
                var deleteParam1 = deleteCommand.CreateParameter();
                deleteParam1.DbType = DbType.Binary;
                deleteParam1.ParameterName = "@identityId";
                deleteCommand.Parameters.Add(deleteParam1);

                deleteParam1.Value = identityId.ToByteArray();
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
            item.modified = (rdr[9] == DBNull.Value) ? item.created : new UnixTimeUtc((long)rdr[9]); // HACK
            return item;
       }

        public virtual async Task<RegistrationsRecord> GetAsync(Guid identityId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,email,primaryDomainName,firstRunToken,disabled,markedForDeletionDate,planId,json,created,modified FROM Registrations " +
                                             "WHERE identityId = @identityId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.DbType = DbType.Binary;
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);

                get0Param1.Value = identityId.ToByteArray();
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
