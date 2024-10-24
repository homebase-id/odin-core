using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.AttestationDatabase;

namespace Odin.Attestation
{
    public static class AttestationDatabaseUtil
    {
        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="_db"></param>
        public static async Task InitializeDatabaseAsync(AttestationDatabase _db, DatabaseConnection conn)
        {
            await _db.CreateDatabaseAsync(dropExistingTables: true); // Remove "true" for production
        }
    }
}
