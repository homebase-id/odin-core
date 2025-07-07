using Odin.Core.Storage.Database.Attestation;

namespace Odin.Attestation
{
    public static class AttestationDatabaseUtil
    {
        /// <summary>
        /// Called once from the controller to make sure database is setup
        /// Need to set drop to false in production
        /// </summary>
        /// <param name="db"></param>
        public static async Task InitializeDatabaseAsync(AttestationDatabase db)
        {
            await db.CreateDatabaseAsync(dropExistingTables: true); // Remove "true" for production
        }
    }
}
