using Odin.Core.Storage.Factory;
using System;
using System.Threading.Tasks;

#nullable enable

namespace Odin.Core.Storage.Database.Identity.Table
{
    public class MigrationException : Exception
    {
        public MigrationException(string message, Exception? inner = null)
            : base(message, inner) { }
    }


    public abstract class Migration
    {
        public abstract int MigrationVersion { get; }
        public abstract Migration DownMigration { get; }

        public int PreviousVersion()
        {
            if (DownMigration == null)
                return 0;
            else
                return DownMigration.MigrationVersion;
        }

        public static async Task<int> RenameAsync(IConnectionWrapper cn, string oldName, string newName)
        {
            await using var renameCommand = cn.CreateCommand();
            {
                renameCommand.CommandText = $"ALTER TABLE {oldName} RENAME TO {newName};";
                return await renameCommand.ExecuteNonQueryAsync();
            }
        }

    }
}
