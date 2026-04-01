// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System;
using System.Collections.Immutable;
using Odin.Notarius.Database;
using Odin.Notarius.Database.Table;

namespace Odin.Notarius.Database;

public partial class NotaryDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableNotaryChain),
    ];

    private Lazy<TableNotaryChain> _notaryChain;
    public TableNotaryChain NotaryChain => LazyResolve(ref _notaryChain);

}
