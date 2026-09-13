using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableAppRegistrationsMigrationList : MigrationListBase
{
    public TableAppRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableAppRegistrationsMigrationV0(-1),
            new TableAppRegistrationsMigrationV202608271000(0),
            // AUTO-INSERT-MARKER
        };
    }

}
