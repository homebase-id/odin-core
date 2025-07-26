using System;
using System.Collections.Generic;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.System.Table;

public class TableCertificatesMigrationList : MigrationListBase
{
    public TableCertificatesMigrationList()
    {
        Migrations = new List<MigrationBase>() {
            new TableCertificatesMigrationV0(-1),
            // AUTO-INSERT-MARKER
        };
    }

}
