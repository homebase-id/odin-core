using Microsoft.Data.Sqlite;
using Npgsql;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#nullable enable

namespace Odin.Core.Storage.Database
{
    public class MigrationException : OdinSystemException
    {
        public MigrationException(string message, Exception? inner = null)
            : base(message, inner) { }
    }


    public abstract class MigrationBase
    {
        public abstract Int64 MigrationVersion { get; }
        public Int64 PreviousVersion { get; }

        protected MigrationBase(Int64 previousVersion)
        {
            PreviousVersion = previousVersion;
        }

        public string MigrationTableName(string tableName, Int64 version)
        {
            return $"{tableName}MigrationsV{PreviousVersion}";
        }

        public async Task CheckSqlTableVersion(IConnectionWrapper cn, string tableName, Int64 versionMustBe)
        {
            var sqlVersion = await MigrationBase.GetTableVersionAsync(cn, tableName);
            if (sqlVersion == -1)
            {
                // Old tables - which are version 0 - don't have an embedded version
                if (MigrationVersion != 0)
                    throw new Exception("Table version not found and table version not zero");
            }
            else
            {
                if (versionMustBe != sqlVersion)
                    throw new Exception($"This function is designed to work on table version {versionMustBe} but found current SQL version {sqlVersion}");
            }
        }


        public static async Task<int> DeleteTableAsync(IConnectionWrapper cn, string tableName)
        {
            await using var deleteCommand = cn.CreateCommand();
            deleteCommand.CommandText = $"DROP TABLE IF EXISTS {tableName};";
            return await deleteCommand.ExecuteNonQueryAsync();
        }

        public static async Task<string> GetTableCommentAsync(IConnectionWrapper cn, string tableName)
        {
            await using var cmd = cn.CreateCommand();

            if (cn.DatabaseType == DatabaseType.Postgres)
            {
                cmd.CommandText = $"SELECT obj_description('{tableName}'::regclass);";
                var result = await cmd.ExecuteScalarAsync();
                return result as string ?? string.Empty;
            }
            else // SQLite
            {
                cmd.CommandText = $"SELECT sql FROM sqlite_master WHERE type = 'table' AND name = '{tableName}';";
                var sql = await cmd.ExecuteScalarAsync() as string;
                if (string.IsNullOrEmpty(sql))
                    return string.Empty;

                var match = Regex.Match(sql, @"--\s*(\{.*?\})");
                return match.Success ? match.Groups[1].Value : string.Empty;
            }
        }


        // -1 means invalid version
        public static async Task<Int64> GetTableVersionAsync(IConnectionWrapper cn, string tableName)
        {
            var comment = await GetTableCommentAsync(cn, tableName);

            if (string.IsNullOrEmpty(comment))
                return -1;
            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, Int64>>(comment);
                if (json == null)
                    return -1;

                return json.TryGetValue("Version", out Int64 version) ? version : -1; // Default if "Version" key is missing
            }
            catch (JsonException)
            {
                return -1; // Handle invalid JSON
            }
        }

        public static async Task CreateTableIfNotExistsAsync(IConnectionWrapper cn, string createSql, string commentSql)
        {
            await using var cmd = cn.CreateCommand();

            try
            {
                cmd.CommandText = createSql;
                await cmd.ExecuteNonQueryAsync();

                if (String.IsNullOrEmpty(commentSql) == false)
                {
                    cmd.CommandText = commentSql;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (OdinDatabaseException ex) when (cn.DatabaseType == DatabaseType.Sqlite &&
                        ex.InnerException is SqliteException sqliteEx &&
                        sqliteEx.SqliteErrorCode == 1 &&
                        sqliteEx.Message.Contains("table") &&
                        sqliteEx.Message.Contains("already exists"))
            {
                // Table already exists in SQLite; silently ignore
            }
            catch (OdinDatabaseException ex) when (cn.DatabaseType == DatabaseType.Postgres &&
                            ex.InnerException is PostgresException pgEx &&
                            pgEx.SqlState == "42P07")
            {
                // Table already exists in PostgreSQL; silently ignore
            }
            catch
            {
                throw;
            }
        }

        public static async Task<int> RenameAsync(IConnectionWrapper cn, string oldName, string newName)
        {
            await using var renameCommand = cn.CreateCommand();
            {
                renameCommand.CommandText = $"ALTER TABLE {oldName} RENAME TO {newName};";
                return await renameCommand.ExecuteNonQueryAsync();
            }
        }

        public static async Task<int> GetCountAsync(IConnectionWrapper cn, string tableName)
        {
            await using var renameCommand = cn.CreateCommand();
            {
                renameCommand.CommandText = $"SELECT COUNT(*) FROM {tableName};";
                var count = await renameCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public static async Task<bool> VerifyRowCount(IConnectionWrapper cn, string sourceTable, string destTable)
        {
            var n1 = await GetCountAsync(cn, sourceTable);
            if (n1 < 0)
                return false;

            var n2 = await GetCountAsync(cn, destTable);
            if (n2 < 0)
                return false;

            return n1 == n2;
        }

        public abstract Task CreateTableIfNotExistsAsync(IConnectionWrapper cn);
        public abstract Task DownAsync(IConnectionWrapper cn);
        public abstract Task UpAsync(IConnectionWrapper cn);
    }
}
