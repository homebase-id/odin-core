using Odin.Core.Exceptions;
using Odin.Core.Storage.Factory;
using System;
using System.Threading.Tasks;

#nullable enable

namespace Odin.Core.Storage.Database
{
    public class MigrationException : OdinSystemException
    {
        public MigrationException(string message, Exception? inner = null)
            : base(message, inner) { }
    }


    public abstract class MigrationBase
    {
        public abstract Int64 MigrationVersion { get; }
        public Int64 PreviousVersion { get; }

        protected MigrationBase(Int64 previousVersion)
        {
            PreviousVersion = previousVersion;
        }

        public string MigrationTableName(string tableName, Int64 version)
        {
            return $"{tableName}MigrationsV{PreviousVersion}";
        }

        public async Task CheckSqlTableVersion(IConnectionWrapper cn, string tableName, Int64 versionMustBe)
        {
            var sqlVersion = await SqlHelper.GetTableVersionAsync(cn, tableName);
            if (sqlVersion == -1)
            {
                // Old tables - which are version 0 - don't have an embedded version
                if (MigrationVersion != 0)
                    throw new Exception("Table version not found and table version not zero");
            }
            else
            {
                if (versionMustBe != sqlVersion)
                    throw new Exception($"This function is designed to work on table version {versionMustBe} but found current SQL version {sqlVersion}");
            }
        }



        public static async Task<bool> VerifyRowCount(IConnectionWrapper cn, string sourceTable, string destTable)
        {
            var n1 = await SqlHelper.GetCountAsync(cn, sourceTable);
            if (n1 < 0)
                return false;

            var n2 = await SqlHelper.GetCountAsync(cn, destTable);
            if (n2 < 0)
                return false;

            return n1 == n2;
        }

        public abstract Task CreateTableWithCommentAsync(IConnectionWrapper cn);
        public abstract Task DownAsync(IConnectionWrapper cn);
        public abstract Task UpAsync(IConnectionWrapper cn);
    }
}
