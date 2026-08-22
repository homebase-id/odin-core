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
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version13tov14
{
    /// <summary>
    /// v13 → v14: moves circle definitions out of the shared key-three-value blob and into the
    /// <c>Circle</c> table.
    ///
    /// <para>
    /// Definitions were stored as opaque blob rows, which is why <c>AppId</c> and <c>GrantOn</c> could
    /// never be queried or constrained. The enrollment pipeline has to answer "which circles enrol on
    /// connect?" on the hot path, and that is a <c>WHERE GrantOn = ?</c> against an indexed column.
    /// <see cref="CircleDefinitionService"/> now reads and writes the table; this copies what is already
    /// there so nothing is lost on the way.
    /// </para>
    ///
    /// <para>
    /// Idempotent, and additive: a definition already present in the table is left alone rather than
    /// overwritten, so a partial run can be repeated safely. The blob rows are deliberately <b>not</b>
    /// deleted -- if this turns out to have gone wrong, the source data is still sitting there. Cleaning
    /// them up is a later, separate job.
    /// </para>
    /// </summary>
    public class V13ToV14VersionMigrationService(
        ILogger<V13ToV14VersionMigrationService> logger,
        IdentityDatabase db,
        TableKeyThreeValueCached tblKeyThreeValue)
    {
        // The context and category keys CircleDefinitionService used while definitions lived in the blob.
        private const string LegacyCircleValueContextKey = "dc1c198c-c280-4b9c-93ce-d417d0a58491";

        private static readonly ThreeKeyValueStorage LegacyCircleStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(LegacyCircleValueContextKey));

        private static readonly byte[] LegacyCircleDataType =
            Guid.Parse("2a915ab8-412e-42d8-b157-a123f107f224").ToByteArray();

        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var legacy = (await LegacyCircleStorage
                .GetByCategoryAsync<CircleDefinition>(tblKeyThreeValue, LegacyCircleDataType) ?? []).ToList();

            if (legacy.Count == 0)
            {
                logger.LogInformation("v13->v14: no circle definitions in blob storage; nothing to move");
                return;
            }

            var copied = 0;
            var skipped = 0;

            foreach (var definition in legacy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (definition?.Id == null)
                {
                    logger.LogWarning("v13->v14: skipping a blob circle definition with no id");
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

                logger.LogDebug("v13->v14: moved circle definition [{name}] {id} into the Circle table",
                    definition.Name, definition.Id);
            }

            logger.LogInformation(
                "v13->v14: moved {copied} circle definition(s) into the Circle table; {skipped} already present",
                copied, skipped);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
    }
}
