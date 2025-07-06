using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Connection;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class TableAttestationRequest(
    CacheHelper cache,
    ScopedIdentityConnectionFactory scopedConnectionFactory) : TableAttestationRequestCRUD(cache, scopedConnectionFactory)
    {
    }
}
