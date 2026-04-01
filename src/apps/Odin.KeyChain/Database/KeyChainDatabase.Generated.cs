// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Immutable;
using Odin.KeyChain.Database.Table;

namespace Odin.KeyChain.Database;

public partial class KeyChainDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableKeyChain),
    ];

    private Lazy<TableKeyChain> _keyChain;
    public TableKeyChain KeyChain => LazyResolve(ref _keyChain);

}
