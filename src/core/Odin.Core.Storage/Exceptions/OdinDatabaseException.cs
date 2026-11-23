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

    /// <summary>
    /// Whether the write broke the unique index on a column starting <paramref name="columnPrefix"/>, as the database's
    /// own error says.  Unlike a read of the clashing row, it works inside a transaction Postgres has aborted.
    /// SQLite's message lists the index's columns ("UNIQUE constraint failed: DriveMainIndex.identityId, ...,
    /// DriveMainIndex.globalTransitId").  Postgres names the constraint after the table it was created on and its
    /// columns, cut to 63 characters and numbered when the name is taken, so pass a prefix every cut keeps
    /// (<see cref="Database.Identity.Table.TableDriveMainIndex.GlobalTransitIdIndexMarker"/>).
    /// </summary>
    public bool IsUniqueConstraintViolationOn(string columnPrefix)
    {
        if (!IsUniqueConstraintViolation)
        {
            return false;
        }

        var named = InnerException switch
        {
            SqliteException sqliteException => sqliteException.Message,
            PostgresException postgresException => postgresException.ConstraintName,
            _ => null
        };

        return named?.Contains(columnPrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

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
