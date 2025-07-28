using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity;

namespace Odin.Core.Storage.Database.Identity;

public class TableConnectionsMigrationList : MigrationListBase
{
    public TableConnectionsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableConnectionsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
