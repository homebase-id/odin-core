using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity.Table;

public class TableAppGrantsMigrationList : MigrationListBase
{
    public TableAppGrantsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppGrantsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
