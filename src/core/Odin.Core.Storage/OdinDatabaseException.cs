using System;
using Microsoft.Data.Sqlite;
using Npgsql;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

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
}
