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
            typeof(TableRegistrations),
            typeof(TableSettings),
    ];

    private Lazy<TableJobs> _jobs;
    public TableJobs Jobs => LazyResolve(ref _jobs);

    private Lazy<TableCertificates> _certificates;
    public TableCertificates Certificates => LazyResolve(ref _certificates);

    private Lazy<TableRegistrations> _registrations;
    public TableRegistrations Registrations => LazyResolve(ref _registrations);

    private Lazy<TableSettings> _settings;
    public TableSettings Settings => LazyResolve(ref _settings);

}
