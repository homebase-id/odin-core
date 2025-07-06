using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.SQLite.NotaryDatabase;
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

namespace Odin.Core.Storage.SQLite.KeyChainDatabase
{
    public class KeyChainDatabase(ILifetimeScope lifetimeScope) :           AbstractDatabase<IIdentityDbConnectionFactory>(lifetimeScope)
    {
        //
        // Put all database tables alphabetically here.
        // Don't forget to add the table to the lazy properties as well.
        //
        public static readonly ImmutableList<Type> TableTypes =
        [
            typeof(TableKeyChain)
        ];

        private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

        //
        // Table convenience properties
        //

        // KeyChain
        private Lazy<TableKeyChain> _keyChain;
        public TableKeyChain KeyChain => LazyResolve(ref _keyChain);


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

        public readonly TableKeyChain tblKeyChain = null;

        private readonly CacheHelper _cache = new CacheHelper("blockchain");
        private readonly string _file;
        private readonly int _line;
        public KeyChainDatabase(string dataSource, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1) : base(dataSource)
        {
            tblKeyChain = new TableKeyChain(_cache, ...seb);

            _file = file;
            _line = line;
        }

        /// <summary>
        /// Will destroy all your data and create a fresh database
        /// </summary>
        public override async Task CreateDatabaseAsync(bool dropExistingTables = true)
        {
            using var conn = CreateDisposableConnection();
            await tblKeyChain.EnsureTableExistsAsync(dropExistingTables);
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