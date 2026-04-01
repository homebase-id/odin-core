using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

public class TableRegistrationsMigrationList : MigrationListBase
{
    public TableRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableRegistrationsMigrationV0(-1),
            new TableRegistrationsMigrationV202508281508(0),
            new TableRegistrationsMigrationV202509090509(202508281508),
            // AUTO-INSERT-MARKER
        };
    }

}
