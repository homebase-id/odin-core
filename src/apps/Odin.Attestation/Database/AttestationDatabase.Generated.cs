// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Immutable;
using Odin.Attestation.Database;
using Odin.Attestation.Database.Table;

namespace Odin.Attestation.Database;

public partial class AttestationDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableAttestationRequest),
            typeof(TableAttestationStatus),
    ];

    private Lazy<TableAttestationRequest> _attestationRequest;
    public TableAttestationRequest AttestationRequest => LazyResolve(ref _attestationRequest);

    private Lazy<TableAttestationStatus> _attestationStatus;
    public TableAttestationStatus AttestationStatus => LazyResolve(ref _attestationStatus);

}
