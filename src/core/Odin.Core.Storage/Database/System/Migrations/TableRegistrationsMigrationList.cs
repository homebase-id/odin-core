using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.System.Migrations;

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
