using Odin.Core.Identity;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.Identity.Abstractions;

public static class OdinIdentityExtensions
{
    public static string BytesToSql(this OdinIdentity odinIdentity, DatabaseType databaseType)
    {
        return odinIdentity.IdAsByteArray().ToSql(databaseType);
    }
}
