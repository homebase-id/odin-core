using System;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Odin.Core.Time;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Core.Storage.Exceptions;
using Odin.Core.Storage.SQLite;

// THIS FILE IS AUTO GENERATED - DO NOT EDIT

namespace Odin.Core.Storage.Database.Identity.Table
{
    public record DriveDefinitionsRecord
    {
        private Int64 _rowId;
        public Int64 rowId
        {
           get {
                   return _rowId;
               }
           set {
                  _rowId = value;
               }
        }
        private Guid _identityId;
        public Guid identityId
        {
           get {
                   return _identityId;
               }
           set {
                  _identityId = value;
               }
        }
        private Guid _DriveId;
        public Guid DriveId
        {
           get {
                   return _DriveId;
               }
           set {
                  _DriveId = value;
               }
        }
        private Guid _TempDriveAlias;
        public Guid TempDriveAlias
        {
           get {
                   return _TempDriveAlias;
               }
           set {
                  _TempDriveAlias = value;
               }
        }
        private Guid _DriveType;
        public Guid DriveType
        {
           get {
                   return _DriveType;
               }
           set {
                  _DriveType = value;
               }
        }
        private string _DriveName;
        public string DriveName
        {
           get {
                   return _DriveName;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null DriveName");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short DriveName, was {value.Length} (min 0)");
                    if (value?.Length > 100) throw new OdinDatabaseValidationException($"Too long DriveName, was {value.Length} (max 100)");
                  _DriveName = value;
               }
        }
        internal string DriveNameNoLengthCheck
        {
           get {
                   return _DriveName;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null DriveName");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short DriveName, was {value.Length} (min 0)");
                  _DriveName = value;
               }
        }
        private string _MasterKeyEncryptedStorageKeyJson;
        public string MasterKeyEncryptedStorageKeyJson
        {
           get {
                   return _MasterKeyEncryptedStorageKeyJson;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null MasterKeyEncryptedStorageKeyJson");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short MasterKeyEncryptedStorageKeyJson, was {value.Length} (min 0)");
                    if (value?.Length > 1024) throw new OdinDatabaseValidationException($"Too long MasterKeyEncryptedStorageKeyJson, was {value.Length} (max 1024)");
                  _MasterKeyEncryptedStorageKeyJson = value;
               }
        }
        internal string MasterKeyEncryptedStorageKeyJsonNoLengthCheck
        {
           get {
                   return _MasterKeyEncryptedStorageKeyJson;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null MasterKeyEncryptedStorageKeyJson");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short MasterKeyEncryptedStorageKeyJson, was {value.Length} (min 0)");
                  _MasterKeyEncryptedStorageKeyJson = value;
               }
        }
        private string _EncryptedIdIv64;
        public string EncryptedIdIv64
        {
           get {
                   return _EncryptedIdIv64;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdIv64");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdIv64, was {value.Length} (min 0)");
                    if (value?.Length > 1024) throw new OdinDatabaseValidationException($"Too long EncryptedIdIv64, was {value.Length} (max 1024)");
                  _EncryptedIdIv64 = value;
               }
        }
        internal string EncryptedIdIv64NoLengthCheck
        {
           get {
                   return _EncryptedIdIv64;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdIv64");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdIv64, was {value.Length} (min 0)");
                  _EncryptedIdIv64 = value;
               }
        }
        private string _EncryptedIdValue64;
        public string EncryptedIdValue64
        {
           get {
                   return _EncryptedIdValue64;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdValue64");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdValue64, was {value.Length} (min 0)");
                    if (value?.Length > 1024) throw new OdinDatabaseValidationException($"Too long EncryptedIdValue64, was {value.Length} (max 1024)");
                  _EncryptedIdValue64 = value;
               }
        }
        internal string EncryptedIdValue64NoLengthCheck
        {
           get {
                   return _EncryptedIdValue64;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null EncryptedIdValue64");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short EncryptedIdValue64, was {value.Length} (min 0)");
                  _EncryptedIdValue64 = value;
               }
        }
        private string _detailsJson;
        public string detailsJson
        {
           get {
                   return _detailsJson;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null detailsJson");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short detailsJson, was {value.Length} (min 0)");
                    if (value?.Length > 21504) throw new OdinDatabaseValidationException($"Too long detailsJson, was {value.Length} (max 21504)");
                  _detailsJson = value;
               }
        }
        internal string detailsJsonNoLengthCheck
        {
           get {
                   return _detailsJson;
               }
           set {
                    if (value == null) throw new OdinDatabaseValidationException("Cannot be null detailsJson");
                    if (value?.Length < 0) throw new OdinDatabaseValidationException($"Too short detailsJson, was {value.Length} (min 0)");
                  _detailsJson = value;
               }
        }
        private UnixTimeUtc _created;
        public UnixTimeUtc created
        {
           get {
                   return _created;
               }
           set {
                  _created = value;
               }
        }
        private UnixTimeUtc? _modified;
        public UnixTimeUtc? modified
        {
           get {
                   return _modified;
               }
           set {
                  _modified = value;
               }
        }
    } // End of record DriveDefinitionsRecord

    public abstract class TableDriveDefinitionsCRUD
    {
        private readonly ScopedIdentityConnectionFactory _scopedConnectionFactory;

        protected TableDriveDefinitionsCRUD(CacheHelper cache, ScopedIdentityConnectionFactory scopedConnectionFactory)
        {
            _scopedConnectionFactory = scopedConnectionFactory;
        }


        public virtual async Task EnsureTableExistsAsync(bool dropExisting = false)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var cmd = cn.CreateCommand();
            if (dropExisting)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS DriveDefinitions;";
                await cmd.ExecuteNonQueryAsync();
            }
            var rowid = "";
            if (_scopedConnectionFactory.DatabaseType == DatabaseType.Postgres)
               rowid = "rowid BIGSERIAL PRIMARY KEY,";
            else
               rowid = "rowId INTEGER PRIMARY KEY AUTOINCREMENT,";
            var wori = "";
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS DriveDefinitions("
                   +rowid
                   +"identityId BYTEA NOT NULL, "
                   +"DriveId BYTEA NOT NULL, "
                   +"TempDriveAlias BYTEA NOT NULL, "
                   +"DriveType BYTEA NOT NULL, "
                   +"DriveName TEXT NOT NULL, "
                   +"MasterKeyEncryptedStorageKeyJson TEXT NOT NULL, "
                   +"EncryptedIdIv64 TEXT NOT NULL, "
                   +"EncryptedIdValue64 TEXT NOT NULL, "
                   +"detailsJson TEXT NOT NULL, "
                   +"created BIGINT NOT NULL, "
                   +"modified BIGINT  "
                   +", UNIQUE(identityId,DriveId)"
                   +$"){wori};"
                   ;
            await cmd.ExecuteNonQueryAsync();
        }
        
        protected virtual async Task<int> InsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.DriveId.AssertGuidNotEmpty("Guid parameter DriveId cannot be set to Empty GUID.");
            item.TempDriveAlias.AssertGuidNotEmpty("Guid parameter TempDriveAlias cannot be set to Empty GUID.");
            item.DriveType.AssertGuidNotEmpty("Guid parameter DriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,DriveId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                             $"VALUES (@identityId,@DriveId,@TempDriveAlias,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},NULL)"+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@DriveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@TempDriveAlias";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@DriveType";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@DriveName";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@EncryptedIdIv64";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@EncryptedIdValue64";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@detailsJson";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.DriveId.ToByteArray();
                insertParam3.Value = item.TempDriveAlias.ToByteArray();
                insertParam4.Value = item.DriveType.ToByteArray();
                insertParam5.Value = item.DriveName;
                insertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                insertParam7.Value = item.EncryptedIdIv64;
                insertParam8.Value = item.EncryptedIdValue64;
                insertParam9.Value = item.detailsJson;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<bool> TryInsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.DriveId.AssertGuidNotEmpty("Guid parameter DriveId cannot be set to Empty GUID.");
            item.TempDriveAlias.AssertGuidNotEmpty("Guid parameter TempDriveAlias cannot be set to Empty GUID.");
            item.DriveType.AssertGuidNotEmpty("Guid parameter DriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var insertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                insertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,DriveId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@TempDriveAlias,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},NULL) " +
                                            "ON CONFLICT DO NOTHING "+
                                            "RETURNING created,modified,rowId;";
                var insertParam1 = insertCommand.CreateParameter();
                insertParam1.ParameterName = "@identityId";
                insertCommand.Parameters.Add(insertParam1);
                var insertParam2 = insertCommand.CreateParameter();
                insertParam2.ParameterName = "@DriveId";
                insertCommand.Parameters.Add(insertParam2);
                var insertParam3 = insertCommand.CreateParameter();
                insertParam3.ParameterName = "@TempDriveAlias";
                insertCommand.Parameters.Add(insertParam3);
                var insertParam4 = insertCommand.CreateParameter();
                insertParam4.ParameterName = "@DriveType";
                insertCommand.Parameters.Add(insertParam4);
                var insertParam5 = insertCommand.CreateParameter();
                insertParam5.ParameterName = "@DriveName";
                insertCommand.Parameters.Add(insertParam5);
                var insertParam6 = insertCommand.CreateParameter();
                insertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                insertCommand.Parameters.Add(insertParam6);
                var insertParam7 = insertCommand.CreateParameter();
                insertParam7.ParameterName = "@EncryptedIdIv64";
                insertCommand.Parameters.Add(insertParam7);
                var insertParam8 = insertCommand.CreateParameter();
                insertParam8.ParameterName = "@EncryptedIdValue64";
                insertCommand.Parameters.Add(insertParam8);
                var insertParam9 = insertCommand.CreateParameter();
                insertParam9.ParameterName = "@detailsJson";
                insertCommand.Parameters.Add(insertParam9);
                insertParam1.Value = item.identityId.ToByteArray();
                insertParam2.Value = item.DriveId.ToByteArray();
                insertParam3.Value = item.TempDriveAlias.ToByteArray();
                insertParam4.Value = item.DriveType.ToByteArray();
                insertParam5.Value = item.DriveName;
                insertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                insertParam7.Value = item.EncryptedIdIv64;
                insertParam8.Value = item.EncryptedIdValue64;
                insertParam9.Value = item.detailsJson;
                await using var rdr = await insertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return true;
                }
                return false;
            }
        }

        protected virtual async Task<int> UpsertAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.DriveId.AssertGuidNotEmpty("Guid parameter DriveId cannot be set to Empty GUID.");
            item.TempDriveAlias.AssertGuidNotEmpty("Guid parameter TempDriveAlias cannot be set to Empty GUID.");
            item.DriveType.AssertGuidNotEmpty("Guid parameter DriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var upsertCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                upsertCommand.CommandText = "INSERT INTO DriveDefinitions (identityId,DriveId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified) " +
                                            $"VALUES (@identityId,@DriveId,@TempDriveAlias,@DriveType,@DriveName,@MasterKeyEncryptedStorageKeyJson,@EncryptedIdIv64,@EncryptedIdValue64,@detailsJson,{sqlNowStr},NULL)"+
                                            "ON CONFLICT (identityId,DriveId) DO UPDATE "+
                                            $"SET TempDriveAlias = @TempDriveAlias,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {sqlNowStr} "+
                                            "RETURNING created,modified,rowId;";
                var upsertParam1 = upsertCommand.CreateParameter();
                upsertParam1.ParameterName = "@identityId";
                upsertCommand.Parameters.Add(upsertParam1);
                var upsertParam2 = upsertCommand.CreateParameter();
                upsertParam2.ParameterName = "@DriveId";
                upsertCommand.Parameters.Add(upsertParam2);
                var upsertParam3 = upsertCommand.CreateParameter();
                upsertParam3.ParameterName = "@TempDriveAlias";
                upsertCommand.Parameters.Add(upsertParam3);
                var upsertParam4 = upsertCommand.CreateParameter();
                upsertParam4.ParameterName = "@DriveType";
                upsertCommand.Parameters.Add(upsertParam4);
                var upsertParam5 = upsertCommand.CreateParameter();
                upsertParam5.ParameterName = "@DriveName";
                upsertCommand.Parameters.Add(upsertParam5);
                var upsertParam6 = upsertCommand.CreateParameter();
                upsertParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                upsertCommand.Parameters.Add(upsertParam6);
                var upsertParam7 = upsertCommand.CreateParameter();
                upsertParam7.ParameterName = "@EncryptedIdIv64";
                upsertCommand.Parameters.Add(upsertParam7);
                var upsertParam8 = upsertCommand.CreateParameter();
                upsertParam8.ParameterName = "@EncryptedIdValue64";
                upsertCommand.Parameters.Add(upsertParam8);
                var upsertParam9 = upsertCommand.CreateParameter();
                upsertParam9.ParameterName = "@detailsJson";
                upsertCommand.Parameters.Add(upsertParam9);
                upsertParam1.Value = item.identityId.ToByteArray();
                upsertParam2.Value = item.DriveId.ToByteArray();
                upsertParam3.Value = item.TempDriveAlias.ToByteArray();
                upsertParam4.Value = item.DriveType.ToByteArray();
                upsertParam5.Value = item.DriveName;
                upsertParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                upsertParam7.Value = item.EncryptedIdIv64;
                upsertParam8.Value = item.EncryptedIdValue64;
                upsertParam9.Value = item.detailsJson;
                await using var rdr = await upsertCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> UpdateAsync(DriveDefinitionsRecord item)
        {
            item.identityId.AssertGuidNotEmpty("Guid parameter identityId cannot be set to Empty GUID.");
            item.DriveId.AssertGuidNotEmpty("Guid parameter DriveId cannot be set to Empty GUID.");
            item.TempDriveAlias.AssertGuidNotEmpty("Guid parameter TempDriveAlias cannot be set to Empty GUID.");
            item.DriveType.AssertGuidNotEmpty("Guid parameter DriveType cannot be set to Empty GUID.");
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var updateCommand = cn.CreateCommand();
            {
                string sqlNowStr;
                if (_scopedConnectionFactory.DatabaseType == DatabaseType.Sqlite)
                    sqlNowStr = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";
                else
                    sqlNowStr = "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000";
                updateCommand.CommandText = "UPDATE DriveDefinitions " +
                                            $"SET TempDriveAlias = @TempDriveAlias,DriveType = @DriveType,DriveName = @DriveName,MasterKeyEncryptedStorageKeyJson = @MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64 = @EncryptedIdIv64,EncryptedIdValue64 = @EncryptedIdValue64,detailsJson = @detailsJson,modified = {sqlNowStr} "+
                                            "WHERE (identityId = @identityId AND DriveId = @DriveId) "+
                                            "RETURNING created,modified,rowId;";
                var updateParam1 = updateCommand.CreateParameter();
                updateParam1.ParameterName = "@identityId";
                updateCommand.Parameters.Add(updateParam1);
                var updateParam2 = updateCommand.CreateParameter();
                updateParam2.ParameterName = "@DriveId";
                updateCommand.Parameters.Add(updateParam2);
                var updateParam3 = updateCommand.CreateParameter();
                updateParam3.ParameterName = "@TempDriveAlias";
                updateCommand.Parameters.Add(updateParam3);
                var updateParam4 = updateCommand.CreateParameter();
                updateParam4.ParameterName = "@DriveType";
                updateCommand.Parameters.Add(updateParam4);
                var updateParam5 = updateCommand.CreateParameter();
                updateParam5.ParameterName = "@DriveName";
                updateCommand.Parameters.Add(updateParam5);
                var updateParam6 = updateCommand.CreateParameter();
                updateParam6.ParameterName = "@MasterKeyEncryptedStorageKeyJson";
                updateCommand.Parameters.Add(updateParam6);
                var updateParam7 = updateCommand.CreateParameter();
                updateParam7.ParameterName = "@EncryptedIdIv64";
                updateCommand.Parameters.Add(updateParam7);
                var updateParam8 = updateCommand.CreateParameter();
                updateParam8.ParameterName = "@EncryptedIdValue64";
                updateCommand.Parameters.Add(updateParam8);
                var updateParam9 = updateCommand.CreateParameter();
                updateParam9.ParameterName = "@detailsJson";
                updateCommand.Parameters.Add(updateParam9);
                updateParam1.Value = item.identityId.ToByteArray();
                updateParam2.Value = item.DriveId.ToByteArray();
                updateParam3.Value = item.TempDriveAlias.ToByteArray();
                updateParam4.Value = item.DriveType.ToByteArray();
                updateParam5.Value = item.DriveName;
                updateParam6.Value = item.MasterKeyEncryptedStorageKeyJson;
                updateParam7.Value = item.EncryptedIdIv64;
                updateParam8.Value = item.EncryptedIdValue64;
                updateParam9.Value = item.detailsJson;
                await using var rdr = await updateCommand.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rdr.ReadAsync())
                {
                    long created = (long) rdr[0];
                    long? modified = (rdr[1] == DBNull.Value) ? null : (long) rdr[1];
                    item.created = new UnixTimeUtc(created);
                    if (modified != null)
                        item.modified = new UnixTimeUtc((long)modified);
                    else
                        item.modified = null;
                    item.rowId = (long) rdr[2];
                    return 1;
                }
                return 0;
            }
        }

        protected virtual async Task<int> GetCountAsync()
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getCountCommand = cn.CreateCommand();
            {
                 // TODO: this is SQLite specific
                getCountCommand.CommandText = "SELECT COUNT(*) FROM DriveDefinitions;";
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
            sl.Add("DriveId");
            sl.Add("TempDriveAlias");
            sl.Add("DriveType");
            sl.Add("DriveName");
            sl.Add("MasterKeyEncryptedStorageKeyJson");
            sl.Add("EncryptedIdIv64");
            sl.Add("EncryptedIdValue64");
            sl.Add("detailsJson");
            sl.Add("created");
            sl.Add("modified");
            return sl;
        }

        // SELECT rowId,identityId,DriveId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified
        protected DriveDefinitionsRecord ReadRecordFromReaderAll(DbDataReader rdr)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.identityId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveId = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.TempDriveAlias = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[3]);
            item.DriveType = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[4]);
            item.DriveNameNoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.MasterKeyEncryptedStorageKeyJsonNoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.EncryptedIdIv64NoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.EncryptedIdValue64NoLengthCheck = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[8];
            item.detailsJsonNoLengthCheck = (rdr[9] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[9];
            item.created = (rdr[10] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[10]);
            item.modified = (rdr[11] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[11]);
            return item;
       }

        protected virtual async Task<int> DeleteAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var delete0Command = cn.CreateCommand();
            {
                delete0Command.CommandText = "DELETE FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId";
                var delete0Param1 = delete0Command.CreateParameter();
                delete0Param1.ParameterName = "@identityId";
                delete0Command.Parameters.Add(delete0Param1);
                var delete0Param2 = delete0Command.CreateParameter();
                delete0Param2.ParameterName = "@DriveId";
                delete0Command.Parameters.Add(delete0Param2);

                delete0Param1.Value = identityId.ToByteArray();
                delete0Param2.Value = DriveId.ToByteArray();
                var count = await delete0Command.ExecuteNonQueryAsync();
                return count;
            }
        }

        protected DriveDefinitionsRecord ReadRecordFromReader0(DbDataReader rdr,Guid identityId,Guid DriveId)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.DriveId = DriveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.TempDriveAlias = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveType = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveNameNoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJsonNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64NoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64NoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJsonNoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        protected virtual async Task<DriveDefinitionsRecord> GetByDriveIdAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get0Command = cn.CreateCommand();
            {
                get0Command.CommandText = "SELECT rowId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1;"+
                                             ";";
                var get0Param1 = get0Command.CreateParameter();
                get0Param1.ParameterName = "@identityId";
                get0Command.Parameters.Add(get0Param1);
                var get0Param2 = get0Command.CreateParameter();
                get0Param2.ParameterName = "@DriveId";
                get0Command.Parameters.Add(get0Param2);

                get0Param1.Value = identityId.ToByteArray();
                get0Param2.Value = DriveId.ToByteArray();
                {
                    using (var rdr = await get0Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader0(rdr,identityId,DriveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveDefinitionsRecord ReadRecordFromReader1(DbDataReader rdr,Guid identityId,Guid DriveType)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.DriveType = DriveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.DriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.TempDriveAlias = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveNameNoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJsonNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64NoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64NoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJsonNoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        protected virtual async Task<List<DriveDefinitionsRecord>> GetByDriveTypeAsync(Guid identityId,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get1Command = cn.CreateCommand();
            {
                get1Command.CommandText = "SELECT rowId,DriveId,TempDriveAlias,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND DriveType = @DriveType;"+
                                             ";";
                var get1Param1 = get1Command.CreateParameter();
                get1Param1.ParameterName = "@identityId";
                get1Command.Parameters.Add(get1Param1);
                var get1Param2 = get1Command.CreateParameter();
                get1Param2.ParameterName = "@DriveType";
                get1Command.Parameters.Add(get1Param2);

                get1Param1.Value = identityId.ToByteArray();
                get1Param2.Value = DriveType.ToByteArray();
                {
                    using (var rdr = await get1Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return new List<DriveDefinitionsRecord>();
                        }
                        var result = new List<DriveDefinitionsRecord>();
                        while (true)
                        {
                            result.Add(ReadRecordFromReader1(rdr,identityId,DriveType));
                            if (!await rdr.ReadAsync())
                                break;
                        }
                        return result;
                    } // using
                } //
            } // using
        }

        protected DriveDefinitionsRecord ReadRecordFromReader2(DbDataReader rdr,Guid identityId,Guid TempDriveAlias,Guid DriveType)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.TempDriveAlias = TempDriveAlias;
            item.DriveType = DriveType;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.DriveId = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveNameNoLengthCheck = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[2];
            item.MasterKeyEncryptedStorageKeyJsonNoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.EncryptedIdIv64NoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdValue64NoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.detailsJsonNoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.created = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[7]);
            item.modified = (rdr[8] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[8]);
            return item;
       }

        protected virtual async Task<DriveDefinitionsRecord> GetByTargetDriveAsync(Guid identityId,Guid TempDriveAlias,Guid DriveType)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get2Command = cn.CreateCommand();
            {
                get2Command.CommandText = "SELECT rowId,DriveId,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND TempDriveAlias = @TempDriveAlias AND DriveType = @DriveType LIMIT 1;"+
                                             ";";
                var get2Param1 = get2Command.CreateParameter();
                get2Param1.ParameterName = "@identityId";
                get2Command.Parameters.Add(get2Param1);
                var get2Param2 = get2Command.CreateParameter();
                get2Param2.ParameterName = "@TempDriveAlias";
                get2Command.Parameters.Add(get2Param2);
                var get2Param3 = get2Command.CreateParameter();
                get2Param3.ParameterName = "@DriveType";
                get2Command.Parameters.Add(get2Param3);

                get2Param1.Value = identityId.ToByteArray();
                get2Param2.Value = TempDriveAlias.ToByteArray();
                get2Param3.Value = DriveType.ToByteArray();
                {
                    using (var rdr = await get2Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader2(rdr,identityId,TempDriveAlias,DriveType);
                        return r;
                    } // using
                } //
            } // using
        }

        protected DriveDefinitionsRecord ReadRecordFromReader3(DbDataReader rdr,Guid identityId,Guid DriveId)
        {
            var result = new List<DriveDefinitionsRecord>();
#pragma warning disable CS0168
            long bytesRead;
#pragma warning restore CS0168
            var guid = new byte[16];
            var item = new DriveDefinitionsRecord();
            item.identityId = identityId;
            item.DriveId = DriveId;
            item.rowId = (rdr[0] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (long)rdr[0];
            item.TempDriveAlias = (rdr[1] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[1]);
            item.DriveType = (rdr[2] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new Guid((byte[])rdr[2]);
            item.DriveNameNoLengthCheck = (rdr[3] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[3];
            item.MasterKeyEncryptedStorageKeyJsonNoLengthCheck = (rdr[4] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[4];
            item.EncryptedIdIv64NoLengthCheck = (rdr[5] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[5];
            item.EncryptedIdValue64NoLengthCheck = (rdr[6] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[6];
            item.detailsJsonNoLengthCheck = (rdr[7] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : (string)rdr[7];
            item.created = (rdr[8] == DBNull.Value) ? throw new Exception("item is NULL, but set as NOT NULL") : new UnixTimeUtc((long)rdr[8]);
            item.modified = (rdr[9] == DBNull.Value) ? null : new UnixTimeUtc((long)rdr[9]);
            return item;
       }

        protected virtual async Task<DriveDefinitionsRecord> GetAsync(Guid identityId,Guid DriveId)
        {
            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var get3Command = cn.CreateCommand();
            {
                get3Command.CommandText = "SELECT rowId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM DriveDefinitions " +
                                             "WHERE identityId = @identityId AND DriveId = @DriveId LIMIT 1;"+
                                             ";";
                var get3Param1 = get3Command.CreateParameter();
                get3Param1.ParameterName = "@identityId";
                get3Command.Parameters.Add(get3Param1);
                var get3Param2 = get3Command.CreateParameter();
                get3Param2.ParameterName = "@DriveId";
                get3Command.Parameters.Add(get3Param2);

                get3Param1.Value = identityId.ToByteArray();
                get3Param2.Value = DriveId.ToByteArray();
                {
                    using (var rdr = await get3Command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                    {
                        if (await rdr.ReadAsync() == false)
                        {
                            return null;
                        }
                        var r = ReadRecordFromReader3(rdr,identityId,DriveId);
                        return r;
                    } // using
                } //
            } // using
        }

        protected virtual async Task<(List<DriveDefinitionsRecord>, UnixTimeUtc? nextCursor, long nextRowId)> PagingByCreatedAsync(int count, Guid identityId, UnixTimeUtc? inCursor, long? rowid)
        {
            if (count < 1)
                throw new Exception("Count must be at least 1.");
            if (count == int.MaxValue)
                count--; // avoid overflow when doing +1 on the param below
            if (inCursor == null)
                inCursor = new UnixTimeUtc(long.MaxValue);
            if (rowid == null)
                rowid = long.MaxValue;

            await using var cn = await _scopedConnectionFactory.CreateScopedConnectionAsync();
            await using var getPaging10Command = cn.CreateCommand();
            {
                getPaging10Command.CommandText = "SELECT rowId,identityId,DriveId,TempDriveAlias,DriveType,DriveName,MasterKeyEncryptedStorageKeyJson,EncryptedIdIv64,EncryptedIdValue64,detailsJson,created,modified FROM DriveDefinitions " +
                                            "WHERE (identityId = @identityId) AND created <= @created AND rowId < @rowId ORDER BY created DESC , rowId DESC LIMIT @count;";
                var getPaging10Param1 = getPaging10Command.CreateParameter();
                getPaging10Param1.ParameterName = "@created";
                getPaging10Command.Parameters.Add(getPaging10Param1);
                var getPaging10Param2 = getPaging10Command.CreateParameter();
                getPaging10Param2.ParameterName = "@rowId";
                getPaging10Command.Parameters.Add(getPaging10Param2);
                var getPaging10Param3 = getPaging10Command.CreateParameter();
                getPaging10Param3.ParameterName = "@count";
                getPaging10Command.Parameters.Add(getPaging10Param3);
                var getPaging10Param4 = getPaging10Command.CreateParameter();
                getPaging10Param4.ParameterName = "@identityId";
                getPaging10Command.Parameters.Add(getPaging10Param4);

                getPaging10Param1.Value = inCursor?.milliseconds;
                getPaging10Param2.Value = rowid;
                getPaging10Param3.Value = count+1;
                getPaging10Param4.Value = identityId.ToByteArray();

                {
                    await using (var rdr = await getPaging10Command.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        var result = new List<DriveDefinitionsRecord>();
                        UnixTimeUtc? nextCursor;
                        long nextRowId;
                        int n = 0;
                        while ((n < count) && await rdr.ReadAsync())
                        {
                            n++;
                            result.Add(ReadRecordFromReaderAll(rdr));
                        } // while
                        if ((n > 0) && await rdr.ReadAsync())
                        {
                                nextCursor = result[n - 1].created;
                                nextRowId = result[n - 1].rowId;
                        }
                        else
                        {
                            nextCursor = null;
                            nextRowId = 0;
                        }
                        return (result, nextCursor, nextRowId);
                    } // using
                } //
            } // using 
        } // PagingGet

    }
}
