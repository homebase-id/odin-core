using System;
using System.Collections.Immutable;
using Odin.Core.Storage.Database.Notary;
using Odin.Core.Storage.Database.Notary.Table;

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableNotaryChain),
    ];

    private Lazy<TableNotaryChain> _notaryChain;
    public TableNotaryChain NotaryChain => LazyResolve(ref _notaryChain);

}
