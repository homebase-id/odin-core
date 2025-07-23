using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.System.Table;

public class TableRegistrationsMigrationList : MigrationListBase
{
    public TableRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableRegistrationsMigrationV0(this),
            // AUTO-INSERT-MARKER
        };
    }

}
