using System.Collections.Generic;
using System.Linq;
using Odin.Core.Storage.Database.Identity.Migrations;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityMigrator
{
    public override List<MigrationBase> SortedMigrations
    {
        get {
            var list = new List<MigrationListBase>()
            {
                new TableDrivesMigrationList(),
                new TableDriveMainIndexMigrationList(),
                new TableDriveTransferHistoryMigrationList(),
                new TableDriveAclIndexMigrationList(),
                new TableDriveTagIndexMigrationList(),
                new TableDriveLocalTagIndexMigrationList(),
                new TableDriveReactionsMigrationList(),
                new TableAppNotificationsMigrationList(),
                new TableCatsMigrationList(),
                new TableCircleMigrationList(),
                new TableCircleMemberMigrationList(),
                new TableConnectionsMigrationList(),
                new TableAppGrantsMigrationList(),
                new TableImFollowingMigrationList(),
                new TableFollowsMeMigrationList(),
                new TableInboxMigrationList(),
                new TableOutboxMigrationList(),
                new TableKeyValueMigrationList(),
                new TableKeyTwoValueMigrationList(),
                new TableKeyThreeValueMigrationList(),
                new TableKeyUniqueThreeValueMigrationList(),
                new TableNonceMigrationList(),
            };

            foreach (var migration in list)
                migration.Validate();

            return list.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
        }
    }
}
