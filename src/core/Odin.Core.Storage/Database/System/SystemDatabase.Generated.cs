// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Immutable;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;

namespace Odin.Core.Storage.Database.System;

public partial class SystemDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableJobs),
            typeof(TableCertificates),
            typeof(TableDkimKeys),
            typeof(TableLastSeen),
            typeof(TableRegistrations),
            typeof(TableSettings),
    ];

    private Lazy<TableJobs> _jobs;
    public TableJobs Jobs => LazyResolve(ref _jobs);

    private Lazy<TableCertificates> _certificates;
    public TableCertificates Certificates => LazyResolve(ref _certificates);

    private Lazy<TableDkimKeys> _dkimKeys;
    public TableDkimKeys DkimKeys => LazyResolve(ref _dkimKeys);

    private Lazy<TableLastSeen> _lastSeen;
    public TableLastSeen LastSeen => LazyResolve(ref _lastSeen);

    private Lazy<TableRegistrations> _registrations;
    public TableRegistrations Registrations => LazyResolve(ref _registrations);

    private Lazy<TableSettings> _settings;
    public TableSettings Settings => LazyResolve(ref _settings);

}
