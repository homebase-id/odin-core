using Odin.Core.Identity;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

public static class IdentityKeyExtensions
{
    public static string BytesToSql(this IdentityKey identityKey, DatabaseType databaseType)
    {
        return identityKey.ToByteArray().ToSql(databaseType);
    }
}
