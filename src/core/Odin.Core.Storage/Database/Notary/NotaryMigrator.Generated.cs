// THIS FILE IS AUTO GENERATED - DO NOT EDIT

using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.Notary.Connection;
using Odin.Core.Storage.Database.Notary.Migrations;

#nullable disable

namespace Odin.Core.Storage.Database.Notary;

public partial class NotaryMigrator
{
    public override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableNotaryChainMigrationList(),
            };

            foreach (var migration in list)
            {
                migration.Validate();
            }

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
