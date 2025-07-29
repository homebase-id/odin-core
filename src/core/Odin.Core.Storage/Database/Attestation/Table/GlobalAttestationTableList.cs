using System;
using System.Collections.Immutable;

namespace Odin.Core.Storage.Database.Attestation.Table;

public class GlobalAttestationTableList
{
    public static readonly ImmutableList<Type> TableList = [
            typeof(TableAttestationRequest),
            typeof(TableAttestationStatus),
    ];

    private Lazy<TableAttestationRequest> _attestationRequest;
    public TableAttestationRequest => LazyResolve(ref _attestationRequest);

    private Lazy<TableAttestationStatus> _attestationStatus;
    public TableAttestationStatus => LazyResolve(ref _attestationStatus);

}
