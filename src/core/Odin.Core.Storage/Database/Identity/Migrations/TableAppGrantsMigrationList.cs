using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableAppGrantsMigrationList : MigrationListBase
{
    public TableAppGrantsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppGrantsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
