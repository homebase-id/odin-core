using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Factory;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/*
=====
Query notes:

https://stackoverflow.com/questions/1711631/improve-insert-per-second-performance-of-sqlite

https://stackoverflow.com/questions/50826767/sqlite-index-performance

https://www.sqlitetutorial.net/sqlite-index/

*/

namespace Odin.Core.Storage.SQLite.NotaryDatabase
{
    public class NotaryDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IIdentityDbConnectionFactory>(lifetimeScope)
    {
        //
        // Put all database tables alphabetically here.
        // Don't forget to add the table to the lazy properties as well.
        //
        public static readonly ImmutableList<Type> TableTypes =
        [
            typeof(TableNotaryChain)
        ];

        private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

        //
        // Table convenience properties
        //

        // AppGrants
        private Lazy<TableNotaryChain> _notaryChain;
        public TableNotaryChain NotaryChain => LazyResolve(ref _notaryChain);


        //
        // Connection
        //
        public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
        {
            var factory = _lifetimeScope.Resolve<ScopedIdentityConnectionFactory>();
            var cn = await factory.CreateScopedConnectionAsync();
            return cn;
        }

        //
        // Transaction
        //
        public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
        {
            var factory = _lifetimeScope.Resolve<ScopedIdentityTransactionFactory>();
            var tx = await factory.BeginStackedTransactionAsync();
            return tx;
        }

        // === SEB STUFF ABOVE TO THE BEST OF MY ABILITY
        // OLD STUFF BELOW...

        public readonly TableNotaryChain tblNotaryChain = null;

        private readonly CacheHelper _cache = new CacheHelper("notarychain");
        private readonly string _file;
        private readonly int _line;
        public NotaryDatabase(string connectionString, long commitFrequencyMs = 50, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(connectionString)
        {
            tblNotaryChain = new TableNotaryChain(_cache, ...seb);

            _file = file;
            _line = line;
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblNotaryChain.EnsureTableExistsAsync(dropExistingTables);
            if (dropExistingTables)
            {
                await conn.VacuumAsync();
            }
        }
        
        // SEB:NOTE this is a temporary hack while we refactor the database code
        public new DatabaseConnection CreateDisposableConnection() 
        {
            return base.CreateDisposableConnection();
        }
        
    }
}