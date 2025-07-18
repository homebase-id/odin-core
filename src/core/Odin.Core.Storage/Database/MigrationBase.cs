using Odin.Core.Storage.Factory;
using System;
using System.Threading.Tasks;

#nullable enable

namespace Odin.Core.Storage.Database
{
    public class MigrationException : Exception
    {
        public MigrationException(string message, Exception? inner = null)
            : base(message, inner) { }
    }


    public abstract class MigrationBase
    {
        public abstract int MigrationVersion { get; }
        public MigrationListBase Container { get; set; }

        protected MigrationBase(MigrationListBase container)
        {
            Container = container;
        }

        public static async Task<int> DeleteTableAsync(IConnectionWrapper cn, string tableName)
        {
            await using var deleteCommand = cn.CreateCommand();
            {
                deleteCommand.CommandText = $"DROP TABLE IF EXISTS {tableName};";
                return await deleteCommand.ExecuteNonQueryAsync();
            }
        }

        public static async Task<int> RenameAsync(IConnectionWrapper cn, string oldName, string newName)
        {
            await using var renameCommand = cn.CreateCommand();
            {
                renameCommand.CommandText = $"ALTER TABLE {oldName} RENAME TO {newName};";
                return await renameCommand.ExecuteNonQueryAsync();
            }
        }

        public static async Task<int> GetCountAsync(IConnectionWrapper cn, string tableName)
        {
            await using var renameCommand = cn.CreateCommand();
            {
                renameCommand.CommandText = $"SELECT COUNT(*) FROM {tableName};";
                var count = await renameCommand.ExecuteScalarAsync();
                if (count == null || count == DBNull.Value || !(count is int || count is long))
                    return -1;
                else
                    return Convert.ToInt32(count);
            }
        }

        public static async Task<bool> VerifyRowCount(IConnectionWrapper cn, string sourceTable, string destTable)
        {
            var n1 = await GetCountAsync(cn, sourceTable);
            if (n1 < 0)
                return false;

            var n2 = await GetCountAsync(cn, destTable);
            if (n2 < 0)
                return false;

            return n1 == n2;
        }

        public abstract Task DownAsync(IConnectionWrapper cn);
        public abstract Task UpAsync(IConnectionWrapper cn);
    }
}
