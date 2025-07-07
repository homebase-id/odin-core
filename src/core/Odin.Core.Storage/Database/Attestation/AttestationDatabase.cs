using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Autofac;
using Odin.Core.Storage.Database.Attestation.Connection;
using Odin.Core.Storage.Factory;
using Odin.Core.Storage.SQLite.AttestationDatabase;

namespace Odin.Core.Storage.Database.Attestation;

public class AttestationDatabase(ILifetimeScope lifetimeScope) : AbstractDatabase<IAttestationDbConnectionFactory>(lifetimeScope)
{
    //
    // Put all database tables alphabetically here.
    // Don't forget to add the table to the lazy properties as well.
    //
    public static readonly ImmutableList<Type> TableTypes =
    [
        typeof(TableAttestationRequest),
        typeof(TableAttestationStatus)
    ];

    private readonly ILifetimeScope _lifetimeScope = lifetimeScope;

    //
    // Table convenience properties
    //

    // TableAttestationRequest
    private Lazy<TableAttestationRequest> _attestationRequest;
    public TableAttestationRequest AttestationRequest => LazyResolve(ref _attestationRequest);

    // TableAttestationRequest
    private Lazy<TableAttestationStatus> _attestationStatus;
    public TableAttestationStatus AttestationStatus => LazyResolve(ref _attestationStatus);

    //
    // Connection
    //
    public override async Task<IConnectionWrapper> CreateScopedConnectionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedAttestationConnectionFactory>();
        var cn = await factory.CreateScopedConnectionAsync();
        return cn;
    }

    //
    // Transaction
    //
    public override async Task<IScopedTransaction> BeginStackedTransactionAsync()
    {
        var factory = _lifetimeScope.Resolve<ScopedAttestationTransactionFactory>();
        var tx = await factory.BeginStackedTransactionAsync();
        return tx;
    }

    //
    // Migration
    //

    // SEB:NOTE this is temporary until we have a proper migration system
    public override async Task CreateDatabaseAsync(bool dropExistingTables = false)
    {
        await using var tx = await BeginStackedTransactionAsync();
        foreach (var tableType in TableTypes)
        {
            var table = (ITableMigrator)_lifetimeScope.Resolve(tableType);
            await table.EnsureTableExistsAsync(dropExistingTables);
        }
        tx.Commit();
    }
}