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
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version12tov13
{
    /// <summary>
    /// v12 -> v13: moves circle definitions and app registrations out of the shared key-three-value blob
    /// and into the <c>Circle</c> and <c>AppRegistrations</c> tables.
    ///
    /// <para>
    /// Both moves are the same job -- take a definition out of an opaque blob row and put it where its
    /// fields can be queried and constrained -- and they ship together, so they are one version step. A
    /// tenant is either on the tables or on the blob; there is no useful state in between.
    /// </para>
    ///
    /// <para>
    /// Circle definitions were blob rows, which is why <c>AppId</c> and <c>GrantOn</c> could never be
    /// queried or constrained. The enrollment pipeline has to answer "which circles enrol on connect?" on
    /// the hot path, and that is a <c>WHERE GrantOn = ?</c> against an indexed column.
    /// <see cref="CircleDefinitionService"/> now reads and writes the table; this copies what is already
    /// there so nothing is lost on the way.
    /// </para>
    ///
    /// <para>
    /// App registrations were blob rows too, where <c>UNIQUE(identityId, AppSlug)</c> could not be
    /// expressed at all -- slug uniqueness would have been a best-effort code check over opaque data.
    /// Since the slug is a wire address other identities resolve against, best-effort is not good enough.
    /// The table also gives <c>Circle.AppId</c> and <c>Drives.AppId</c> a real target. Every registration
    /// needs a slug and none of them has one, so this coins them: the known system apps get their obvious
    /// name (chat, feed, mail, photo, owner), everything else is derived from its display name. Slugs are
    /// resolved for the <b>whole set first</b> and the run aborts if any app cannot be given a unique,
    /// valid one -- a half-migrated app table with a slug collision is much worse than not starting.
    /// </para>
    ///
    /// <para>
    /// Idempotent, and additive: a row already present in the destination table is left alone rather than
    /// overwritten, so a partial run can be repeated safely and an app's slug is never reassigned. The
    /// blob rows are deliberately <b>not</b> deleted -- if this turns out to have gone wrong, the source
    /// data is still sitting there. Cleaning them up is a later, separate job.
    /// </para>
    /// </summary>
    public class V12ToV13VersionMigrationService(
        ILogger<V12ToV13VersionMigrationService> logger,
        IdentityDatabase db,
        TableKeyThreeValueCached tblKeyThreeValue)
    {
        // The context and category keys CircleDefinitionService used while definitions lived in the blob.
        private const string LegacyCircleValueContextKey = "dc1c198c-c280-4b9c-93ce-d417d0a58491";

        private static readonly ThreeKeyValueStorage LegacyCircleStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(LegacyCircleValueContextKey));

        private static readonly byte[] LegacyCircleDataType =
            Guid.Parse("2a915ab8-412e-42d8-b157-a123f107f224").ToByteArray();

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

            await MoveCircleDefinitionsAsync(cancellationToken);
            await MoveAppRegistrationsAsync(cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ValidateCircleDefinitionsAsync(cancellationToken);
            await ValidateAppRegistrationsAsync(cancellationToken);
        }

        //

        private async Task MoveCircleDefinitionsAsync(CancellationToken cancellationToken)
        {
            var legacy = (await LegacyCircleStorage
                .GetByCategoryAsync<CircleDefinition>(tblKeyThreeValue, LegacyCircleDataType) ?? []).ToList();

            if (legacy.Count == 0)
            {
                logger.LogInformation("v12->v13: no circle definitions in blob storage; nothing to move");
                return;
            }

            var copied = 0;
            var skipped = 0;

            foreach (var definition in legacy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (definition?.Id == null)
                {
                    logger.LogWarning("v12->v13: skipping a blob circle definition with no id");
                    continue;
                }

                if (await db.CircleCached.GetAsync(definition.Id) != null)
                {
                    skipped++;
                    continue;
                }

                // The four promoted fields were never in the blob, so they take their defaults here:
                // AppId null (an owner circle), GrantOn None (manual membership only), Designation
                // Personal, Emoji null. That is exactly what every pre-existing circle is.
                await db.CircleCached.UpsertAsync(CircleDefinitionService.ToRecord(definition));
                copied++;

                logger.LogDebug("v12->v13: moved circle definition [{name}] {id} into the Circle table",
                    definition.Name, definition.Id);
            }

            logger.LogInformation(
                "v12->v13: moved {copied} circle definition(s) into the Circle table; {skipped} already present",
                copied, skipped);
        }

        private async Task MoveAppRegistrationsAsync(CancellationToken cancellationToken)
        {
            var legacy = (await LegacyAppRegStorage
                    .GetByCategoryAsync<AppRegistration>(tblKeyThreeValue, LegacyAppRegDataType) ?? [])
                .Where(a => a?.AppId != null)
                .ToList();

            if (legacy.Count == 0)
            {
                logger.LogInformation("v12->v13: no app registrations in blob storage; nothing to move");
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
                    $"v12->v13: slug assignment produced {distinct} unique slugs for {slugs.Count} apps");
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

                logger.LogInformation("v12->v13: moved app [{name}] {id} into AppRegistrations as '{slug}'",
                    appReg.Name, appReg.AppId, appReg.AppSlug);
            }

            logger.LogInformation("v12->v13: moved {moved} app registration(s); {skipped} already present",
                moved, skipped);
        }

        private async Task ValidateCircleDefinitionsAsync(CancellationToken cancellationToken)
        {
            var legacy = (await LegacyCircleStorage
                .GetByCategoryAsync<CircleDefinition>(tblKeyThreeValue, LegacyCircleDataType) ?? []).ToList();

            foreach (var definition in legacy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (definition?.Id == null)
                {
                    continue;
                }

                if (await db.CircleCached.GetAsync(definition.Id) == null)
                {
                    throw new OdinSystemException(
                        $"Validation failed: circle definition {definition.Id} is in blob storage but not in the Circle table");
                }
            }
        }

        private async Task ValidateAppRegistrationsAsync(CancellationToken cancellationToken)
        {
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
