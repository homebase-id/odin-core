using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.KeyChain.Connection;
using Odin.Core.Storage.Database.System.Connection;
using Odin.Core.Storage.Factory;

namespace Odin.Core.Storage.Database.KeyChain;

#nullable enable

[SuppressMessage("ReSharper", "ExplicitCallerInfoArgument")]
public partial class KeyChainDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IKeyChainDbConnectionFactory>(lifetimeScope)
{
    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //

    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync(
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        var factory = _lifetimeScope.Resolve<ScopedSystemConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync(filePath, lineNumber);
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Unspecified,
        CancellationToken cancellationToken = default,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        var factory = _lifetimeScope.Resolve<ScopedSystemTransactionFactory>();
        var tx = await factory.BeginStackedTransactionAsync(isolationLevel, cancellationToken, filePath, lineNumber);
        return tx;
    }

    //
    // Migration
    //

    public override async Task MigrateDatabaseAsync()
    {
        var migrator = _lifetimeScope.Resolve<KeyChainMigrator>();
        await migrator.MigrateAsync();
    }
}