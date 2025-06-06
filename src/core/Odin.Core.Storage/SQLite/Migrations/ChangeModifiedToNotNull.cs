using System.Threading.Tasks;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

namespace Odin.Core.Storage.SQLite.Migrations;

//
// MIGRATION steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Build and deploy docker image with migration code - DO NOT START IT
//  - Change to directory /identity-host/data/
//  - Backup the system: sudo zip -r backup-system.zip system
//  - Change to directory /identity-host/data/tenants
//  - Backup the registrations: sudo zip -r backup-registrations.zip registrations
//  - Change to directory /identity-host
//  - Edit the docker-compose.yml file:
//    - Add the correct command line param to start the migration
//    - Disable start-always if enabled
//  - Start the docker image: docker compose up
//  - Wait for the migration to finish
//  - Make sure docker container is gone: docker container prune -f
//  - Redeploy the docker image (this will overwrite the compose changes from above) - START IT
//  - Run some smoke tests
//  - Check the logs for errors
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//
// ROLLBACK steps
//
//  - Change to directory /identity-host
//  - Make sure container is stopped: docker compose down && docker container prune -f
//  - Change to directory /identity-host/data/
//  - Remove system: sudo rm -rf system
//  - Restore system: sudo unzip backup-system.zip
//  - Change to directory /identity-host/data/tenants
//  - Remove registrations: sudo rm -rf registrations
//  - Restore registrations: sudo unzip backup-registrations.zip
//  - Redeploy the docker image (this will overwrite the compose changes) - START IT
//  - Change to directory /identity-host/data/
//  - Clean up: sudo rm backup-system.zip
//  - Change to directory /identity-host/data/tenants/registrations
//  - Clean up: sudo find . -type f -name 'oldidentity.*' -delete
//  - Clean up: sudo rm backup-registrations.zip
//

// Local test:
//
//   mkdir $HOME/tmp/example
//   rsync -rvz yagni.dk:/identity-host/data/system $HOME/tmp/example/data
//   rsync -rvz yagni.dk:/identity-host/data/tenants/registrations $HOME/tmp/example/data/tenants
// run params:
//   --change-modified-not-null

// PROD:
//
// run params:
//   --change-modified-not-null

public static class ChangeModifiedToNotNull
{
    public static async Task ExecuteAsync(SystemDatabase db)
    {
        await using var tx = await db.BeginStackedTransactionAsync();

        var command = tx.CreateCommand();

        command.CommandText =
            """
            UPDATE Jobs SET modified = created WHERE modified IS NULL;
            """;

        await command.ExecuteNonQueryAsync();

        tx.Commit();
    }

    public static async Task ExecuteAsync(IdentityDatabase db)
    {
        await using var tx = await db.BeginStackedTransactionAsync();

        var command = tx.CreateCommand();

        command.CommandText =
            """
            UPDATE Drives SET modified = created WHERE modified IS NULL;
            UPDATE DriveMainIndex SET modified = created WHERE modified IS NULL;
            UPDATE AppNotifications SET modified = created WHERE modified IS NULL;
            UPDATE Connections SET modified = created WHERE modified IS NULL;
            UPDATE ImFollowing SET modified = created WHERE modified IS NULL;
            UPDATE FollowsMe SET modified = created WHERE modified IS NULL;
            UPDATE Inbox SET modified = created WHERE modified IS NULL;
            UPDATE Outbox SET modified = created WHERE modified IS NULL;
            """;

        await command.ExecuteNonQueryAsync();

        tx.Commit();
    }
}

