using System;
using System.Collections.Immutable;

namespace Odin.Core.Storage.Database.Notary.Table;

public class GlobalNotaryTableList
{
    public static readonly ImmutableList<Type> TableList = [
            typeof(TableNotaryChain),
    ];

    private Lazy<TableNotaryChain> _notaryChain;
    public TableNotaryChain => LazyResolve(ref _notaryChain);

}
