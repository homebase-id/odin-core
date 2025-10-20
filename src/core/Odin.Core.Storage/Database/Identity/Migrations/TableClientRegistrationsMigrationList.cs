using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableClientRegistrationsMigrationList : MigrationListBase
{
    public TableClientRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableClientRegistrationsMigrationV0(-1),
            new TableClientRegistrationsMigrationV202510201056(0),
            // AUTO-INSERT-MARKER
        };
    }

}
