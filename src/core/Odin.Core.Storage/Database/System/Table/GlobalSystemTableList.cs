using System;
using System.Collections.Immutable;

namespace Odin.Core.Storage.Database.System.Table;

public class GlobalSystemTableList
{
    public static readonly ImmutableList<Type> TableList = [
            typeof(TableJobs),
            typeof(TableCertificates),
            typeof(TableRegistrations),
            typeof(TableSettings),
    ];

    private Lazy<TableJobs> _jobs;
    public TableJobs => LazyResolve(ref _jobs);

    private Lazy<TableCertificates> _certificates;
    public TableCertificates => LazyResolve(ref _certificates);

    private Lazy<TableRegistrations> _registrations;
    public TableRegistrations => LazyResolve(ref _registrations);

    private Lazy<TableSettings> _settings;
    public TableSettings => LazyResolve(ref _settings);

}
