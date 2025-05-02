using System;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage;

public class OdinDatabaseVersionTagMismatchException : OdinDatabaseException
{

    public OdinDatabaseVersionTagMismatchException(DatabaseType databaseType, string message) : base(databaseType, message)
    {
    }

    public OdinDatabaseVersionTagMismatchException(DatabaseType databaseType, string message, Exception innerException) : base(databaseType, message, innerException)
    {
    }
}
