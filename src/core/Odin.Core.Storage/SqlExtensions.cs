using Odin.Core.Storage.Factory;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Odin.Core.Storage;

#nullable enable

//

public static class ConnectionWrapperExtensions
{
    public static async Task<bool> TableExistsAsync(
        this IConnectionWrapper connectionWrapper,
        string tableName,
        string schema = "public")
    {
        var sql = connectionWrapper.DatabaseType switch
        {
            DatabaseType.Sqlite => $"""
                                    SELECT EXISTS (
                                      SELECT 1 FROM sqlite_master
                                      WHERE type = 'table' 
                                      AND LOWER(name) = LOWER('{tableName}')
                                    );
                                    """,
            DatabaseType.Postgres => $"""
                                      SELECT EXISTS (
                                        SELECT 1 FROM pg_tables 
                                        WHERE LOWER(schemaname) = LOWER('{schema}') 
                                        AND LOWER(tablename) = LOWER('{tableName}')
                                      );
                                      """,
            _ => throw new NotSupportedException($"Database type {connectionWrapper.DatabaseType} not supported")
        };

        await using var cmd = connectionWrapper.CreateCommand();
        cmd.CommandText = sql;

        var rs = await cmd.ExecuteScalarAsync();

        return connectionWrapper.DatabaseType switch
        {
            DatabaseType.Sqlite => Convert.ToInt32(rs) > 0,
            DatabaseType.Postgres => Convert.ToBoolean(rs),
            _ => false
        };
    }
}

//

public static class CommandWrapperExtensions
{
    public static string SqlNow(this ICommandWrapper commandWrapper) 
    { 
        return commandWrapper.DatabaseType 
            switch {
                DatabaseType.Sqlite => "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)",
                DatabaseType.Postgres => "EXTRACT(EPOCH FROM NOW() AT TIME ZONE 'UTC') * 1000",
                _ => throw new NotSupportedException($"Database type {commandWrapper.DatabaseType} not supported") 
            };
    }

    public static string SqlMax(this ICommandWrapper commandWrapper)
    {
        return commandWrapper.DatabaseType
            switch
        {
            DatabaseType.Sqlite => "MAX",
            DatabaseType.Postgres => "GREATEST",
            _ => throw new NotSupportedException($"Database type {commandWrapper.DatabaseType} not supported")
        };
    }

    public static void AddParameter(
        this ICommandWrapper commandWrapper,
        string parameterName,
        DbType dbType,
        object? value)
    {
        var param = commandWrapper.CreateParameter();
        param.ParameterName = parameterName;
        param.DbType = dbType;
        param.Value = value ?? DBNull.Value;
        commandWrapper.Parameters.Add(param);
    }
}

public static class SqlExtensions
{
    public static string ToSql(this byte[] bytes, DatabaseType databaseType)
    {
        return databaseType switch
        {
            DatabaseType.Sqlite => $"x'{Convert.ToHexString(bytes)}'",
            DatabaseType.Postgres => $"'\\x{Convert.ToHexString(bytes)}'",
            _ => throw new NotSupportedException($"Database type {databaseType} not supported")
        };
    }

    public static string BytesToSql(this Guid guid, DatabaseType databaseType)
    {
        return guid.ToByteArray().ToSql(databaseType);
    }
}

//

public static class SqlHelper
{
    public static async Task DeleteTableAsync(IConnectionWrapper cn, string tableName)
    {
        await using var deleteCommand = cn.CreateCommand();
        deleteCommand.CommandText = $"DROP TABLE IF EXISTS {tableName};";
        await deleteCommand.ExecuteNonQueryAsync();
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


    /// <summary>
    /// Create a table and add a version COMMENT (JSON objectg with version info). It's important that this
    /// operation is idempotent so that the comment is only added if the table is created. That's why special
    /// code is required below for PostgressSQL
    /// </summary>
    /// <param name="cn"></param>
    /// <param name="tableName"></param>
    /// <param name="createSql"></param>
    /// <param name="commentSql"></param>
    /// <returns></returns>
    public static async Task CreateTableWithCommentAsync(IConnectionWrapper cn, string tableName, string createSql, string commentSql)
    {
        await using var cmd = cn.CreateCommand();

        if (cn.DatabaseType == DatabaseType.Sqlite)
        {
            // SQLite is idempotent because the comment is embedded in the table SQL
            cmd.CommandText = createSql;
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        // PostgresSQL
        var sql =
          $"""
                DO $$
                BEGIN
                    IF to_regclass('{tableName}') IS NULL THEN
                        {createSql}
                        {commentSql}
                    END IF;
                END $$;
                """;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }


}