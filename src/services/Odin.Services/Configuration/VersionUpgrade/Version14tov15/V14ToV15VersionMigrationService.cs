using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade.Version14tov15
{
    /// <summary>
    /// v14 → v15: moves app registrations out of the shared key-three-value blob and into the
    /// <c>AppRegistrations</c> table.
    ///
    /// <para>
    /// Registrations were blob rows, where <c>UNIQUE(identityId, AppSlug)</c> could not be expressed at
    /// all -- slug uniqueness would have been a best-effort code check over opaque data. Since the slug
    /// is a wire address other identities resolve against, best-effort is not good enough. The table also
    /// gives <c>Circle.AppId</c> and <c>Drives.AppId</c> a real target.
    /// </para>
    ///
    /// <para>
    /// Every registration needs a slug and none of them has one, so this coins them. The known system
    /// apps get their obvious name (chat, feed, mail, photo, owner); everything else is derived from its
    /// display name. Slugs are resolved for the <b>whole set first</b> and the run aborts if any app
    /// cannot be given a unique, valid one -- a half-migrated app table with a slug collision is much
    /// worse than not starting.
    /// </para>
    ///
    /// <para>
    /// Idempotent and additive: an app already in the table is skipped rather than overwritten, so its
    /// slug is never reassigned. The blob rows are deliberately left in place as a fallback.
    /// </para>
    /// </summary>
    public class V14ToV15VersionMigrationService(
        ILogger<V14ToV15VersionMigrationService> logger,
        IdentityDatabase db,
        TableKeyThreeValueCached tblKeyThreeValue)
    {
        // The context and category keys AppRegistrationService used while registrations lived in the blob.
        private const string LegacyAppRegContextKey = "661e097f-6aa5-459f-a445-a9ea65348fde";

        private static readonly ThreeKeyValueStorage LegacyAppRegStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(LegacyAppRegContextKey));

        private static readonly byte[] LegacyAppRegDataType =
            Guid.Parse("14c83583-acfd-4368-89ad-6566636ace3d").ToByteArray();

        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var legacy = (await LegacyAppRegStorage
                    .GetByCategoryAsync<AppRegistration>(tblKeyThreeValue, LegacyAppRegDataType) ?? [])
                .Where(a => a?.AppId != null)
                .ToList();

            if (legacy.Count == 0)
            {
                logger.LogInformation("v14->v15: no app registrations in blob storage; nothing to move");
                return;
            }

            // Resolve every slug before writing anything.  GenerateAll throws rather than returning a
            // duplicate, so a name collision fails the migration with the source data untouched.
            var slugs = AppSlugGenerator.GenerateAll(
                legacy.Select(a => ((Guid)a.AppId, (string)a.Name)));

            var distinct = slugs.Values.Distinct(StringComparer.Ordinal).Count();
            if (distinct != slugs.Count)
            {
                throw new OdinSystemException(
                    $"v14->v15: slug assignment produced {distinct} unique slugs for {slugs.Count} apps");
            }

            var moved = 0;
            var skipped = 0;

            foreach (var appReg in legacy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await db.AppRegistrations.GetAsync(appReg.AppId) != null)
                {
                    // Already migrated. Leave its slug alone -- reassigning one would change an address
                    // other identities may already hold.
                    skipped++;
                    continue;
                }

                appReg.AppSlug = slugs[appReg.AppId];

                await db.AppRegistrations.UpsertAsync(AppRegistrationService.ToRecord(appReg));
                moved++;

                logger.LogInformation("v14->v15: moved app [{name}] {id} into AppRegistrations as '{slug}'",
                    appReg.Name, appReg.AppId, appReg.AppSlug);
            }

            logger.LogInformation("v14->v15: moved {moved} app registration(s); {skipped} already present",
                moved, skipped);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var legacy = (await LegacyAppRegStorage
                    .GetByCategoryAsync<AppRegistration>(tblKeyThreeValue, LegacyAppRegDataType) ?? [])
                .Where(a => a?.AppId != null)
                .ToList();

            foreach (var appReg in legacy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var record = await db.AppRegistrations.GetAsync(appReg.AppId);

                if (record == null)
                {
                    throw new OdinSystemException(
                        $"Validation failed: app {appReg.AppId} is in blob storage but not in AppRegistrations");
                }

                if (!AppSlugGenerator.IsValid(record.AppSlug))
                {
                    throw new OdinSystemException(
                        $"Validation failed: app {appReg.AppId} has an invalid slug '{record.AppSlug}'");
                }
            }
        }
    }
}
