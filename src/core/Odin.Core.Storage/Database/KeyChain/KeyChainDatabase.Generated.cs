using System;
using System.Collections.Immutable;
using Odin.Core.Storage.Database.KeyChain;
using Odin.Core.Storage.Database.KeyChain.Table;

namespace Odin.Core.Storage.Database.KeyChain;

public partial class KeyChainDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableKeyChain),
    ];

    private Lazy<TableKeyChain> _keyChain;
    public TableKeyChain KeyChain => LazyResolve(ref _keyChain);

}
