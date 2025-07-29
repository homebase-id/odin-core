using System;
using System.Collections.Immutable;

namespace Odin.Core.Storage.Database.KeyChain.Table;

public class GlobalKeyChainTableList
{
    public static readonly ImmutableList<Type> TableList = [
            typeof(TableKeyChain),
    ];

    private Lazy<TableKeyChain> _keyChain;
    public TableKeyChain => LazyResolve(ref _keyChain);

}
