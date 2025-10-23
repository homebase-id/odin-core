using System;
using System.Collections.Generic;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public class TableClientRegistrationsMigrationList : MigrationListBase
{
    public TableClientRegistrationsMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableClientRegistrationsMigrationV202510201056(-1),
            new TableClientRegistrationsMigrationV0(202510201056),
            // AUTO-INSERT-MARKER
        };
    }

}
