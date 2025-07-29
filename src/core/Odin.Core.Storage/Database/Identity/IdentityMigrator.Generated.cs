using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Storage.Database.Identity.Migrations;

public partial class GlobalIdentityMigrationList
{
    List<MigrationListBase> MigrationList { get; init; }
    public List<MigrationBase> SortedMigrations { get; init; }

    public GlobalIdentityMigrationList()
    {
        MigrationList = new List<MigrationListBase>() {
            new TableDrivesMigrationList(),
            new TableDriveMainIndexMigrationList(),
            new TableDriveTransferHistoryMigrationList(),
            new TableDriveAclIndexMigrationList(),
            new TableDriveTagIndexMigrationList(),
            new TableDriveLocalTagIndexMigrationList(),
            new TableDriveReactionsMigrationList(),
            new TableAppNotificationsMigrationList(),
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
        foreach (var migration in MigrationList)
            migration.Validate();

        SortedMigrations = MigrationList.SelectMany(migrationList => migrationList.Migrations).OrderBy(migration => migration.MigrationVersion).ToList();
    }
}
