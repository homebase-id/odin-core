using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Autofac;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Factory;
using Odin.KeyChain.Database.Connection;

namespace Odin.KeyChain.Database;

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
        var factory = _lifetimeScope.Resolve<ScopedKeyChainConnectionFactory>();
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
        var factory = _lifetimeScope.Resolve<ScopedKeyChainTransactionFactory>();
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