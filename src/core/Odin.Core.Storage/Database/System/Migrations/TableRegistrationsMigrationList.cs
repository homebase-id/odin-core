using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.System;

namespace Odin.Core.Storage.Database.System;

public class TableRegistrationsMigrationList : MigrationListBase
{
    public TableRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableRegistrationsMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
