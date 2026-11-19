using System;
using Microsoft.Data.Sqlite;
using Npgsql;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Exceptions;

// SQLite error codes: https://www.sqlite.org/rescode.html
// PostgreSQL error codes: https://www.postgresql.org/docs/current/errcodes-appendix.html

public class OdinDatabaseException : OdinSystemException
{
    public DatabaseType DatabaseType { get; }

    public OdinDatabaseException(DatabaseType databaseType, string message) : base(message)
    {
        DatabaseType = databaseType;
    }

    public OdinDatabaseException(DatabaseType databaseType, string message, Exception innerException) : base(message, innerException)
    {
        DatabaseType = databaseType;
    }

    public bool IsUniqueConstraintViolation => IsUniqueConstraintViolationError();
    private bool IsUniqueConstraintViolationError()
    {
        if (InnerException == null)
        {
            return false;
        }

        if (DatabaseType == DatabaseType.Sqlite && InnerException is SqliteException sqliteException)
        {
            return sqliteException.SqliteErrorCode == 19;
        }

        if (DatabaseType == DatabaseType.Postgres && InnerException is PostgresException postgresException)
        {
            return postgresException.SqlState == "23505";
        }

        return false;
    }

    /// <summary>
    /// A unique violation on an index that includes <paramref name="column"/>.  Read from the error itself, not by looking
    /// the rows up: inside a transaction Postgres refuses every statement after the failed one.  SQLite names the columns
    /// in its message ("UNIQUE constraint failed: table.a, table.b"); Postgres in the constraint's name and the detail.
    /// </summary>
    public bool IsUniqueConstraintViolationOn(string column)
    {
        if (!IsUniqueConstraintViolation)
        {
            return false;
        }

        return InnerException switch
        {
            SqliteException e => e.Message.Contains($".{column}", StringComparison.OrdinalIgnoreCase),
            PostgresException e => (e.ConstraintName?.Contains(column, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                   (e.Detail?.Contains(column, StringComparison.OrdinalIgnoreCase) ?? false),
            _ => false
        };
    }
}
