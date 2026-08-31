#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Configuration.VersionUpgrade.Version12tov13
{
    /// <summary>
    /// Circle definitions and app registrations as they were stored before v13: rows in the shared
    /// key-three-value blob.  Read-only.
    /// </summary>
    /// <remarks>
    /// Two callers, for two different reasons.  <see cref="V12ToV13VersionMigrationService"/> reads these
    /// to move them into the tables.  <see cref="CircleDefinitionService"/> and
    /// <see cref="AppRegistrationService"/> read them to answer questions on a tenant that has not been
    /// through that move yet -- the upgrade only runs when the owner logs in, and until it does the
    /// tables are empty while the services read nothing else.  An identity in that state would otherwise
    /// have no apps and no circles: app clients would fail to authenticate and every circle would look
    /// disabled.
    /// <para>
    /// <b>Temporary, and gated on the version rather than on an empty table.</b>  The move deliberately
    /// does not delete the blob rows, so "the table has no such row" is not the same question as "this
    /// tenant has not migrated": after the move, a row missing from the table is an app or circle the
    /// owner deleted, and falling back to the blob would resurrect it.  <see cref="IsPreMoveAsync"/> is
    /// the only correct trigger, and it stops being true the moment the tenant upgrades.
    /// </para>
    /// <para>
    /// Delete this class, its two call sites and the fallbacks in the services once every environment
    /// reports v13 or later -- <c>LogTenantVersions</c> is what makes that a checkable condition rather
    /// than a guess.
    /// </para>
    /// </remarks>
    public sealed class LegacyDefinitionStore(IdentityDatabase db, TableKeyThreeValueCached tblKeyThreeValue)
    {
        /// <summary>The version at which the definitions became table rows.</summary>
        public const int MovedInVersion = 13;

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

        private static readonly SingleKeyValueStorage ConfigStorage =
            TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(TenantConfigService.ConfigContextKey));

        /// <summary>
        /// Whether this tenant still has its definitions in the blob.
        /// </summary>
        /// <remarks>
        /// Read straight from the config store rather than through <see cref="TenantConfigService"/>:
        /// that service reaches circles and provisioning, and the services asking this question are
        /// underneath it.  The row is served by <c>KeyValueCached</c>, so this is not a database round
        /// trip on every read.
        /// </remarks>
        public async Task<bool> IsPreMoveAsync()
        {
            var info = await ConfigStorage.GetAsync<TenantVersionInfo>(db.KeyValueCached, TenantVersionInfo.Key);
            return (info?.DataVersionNumber ?? 0) < MovedInVersion;
        }

        public async Task<List<CircleDefinition>> ReadCircleDefinitionsAsync()
        {
            var legacy = await LegacyCircleStorage
                .GetByCategoryAsync<CircleDefinition>(tblKeyThreeValue, LegacyCircleDataType);

            // The four promoted fields were never in the blob, so they hold their defaults: AppId null
            // (an owner circle), GrantOn None, Designation Personal, Emoji null.  That is exactly what
            // every pre-existing circle is, and what the move writes.
            return (legacy ?? []).Where(c => c?.Id != null).ToList();
        }

        /// <summary>
        /// The blob registrations, each carrying the slug the move would coin for it.
        /// </summary>
        /// <remarks>
        /// Slugs are assigned over the whole set, by the same generator the move uses, so an app answers
        /// to the same address before and after the tenant upgrades.  Nothing here is written.
        /// </remarks>
        public async Task<List<AppRegistration>> ReadAppRegistrationsAsync()
        {
            var legacy = await ReadLegacyAsync();
            if (legacy.Count == 0)
            {
                return [];
            }

            var slugs = AppSlugGenerator.GenerateAll(legacy.Select(a => ((Guid)a.AppId, (string?)a.Name)));

            return legacy.Select(a =>
            {
                var reg = a.ToAppRegistration();
                reg.AppSlug = slugs[a.AppId];
                return reg;
            }).ToList();
        }

        internal async Task<List<LegacyAppRegistration>> ReadLegacyAsync()
        {
            var legacy = await LegacyAppRegStorage
                .GetByCategoryAsync<LegacyAppRegistration>(tblKeyThreeValue, LegacyAppRegDataType);

            return (legacy ?? []).Where(a => a?.AppId != null).ToList();
        }

        /// <summary>
        /// The blob shape of an app registration, frozen as it was before the columns were promoted.
        /// </summary>
        /// <remarks>
        /// <see cref="AppRegistration"/> cannot be used to read these rows.  It <c>[JsonIgnore]</c>s
        /// AppId, AppSlug, Name and CorsHostName, because those are columns now and a second copy in
        /// <c>grantJson</c> could drift from them -- correct for writing, and fatal for reading the
        /// blob, where those very fields are the only place the values exist.  Deserializing a legacy
        /// row into the current type yields an AppId of null for every app, which is silent: the
        /// migration reports nothing to move and commits, leaving the identity with no apps.
        /// <para>
        /// Reading history means holding the shape history was written in, the same way this file
        /// freezes the legacy context and category keys.
        /// </para>
        /// </remarks>
#nullable disable
        internal class LegacyAppRegistration
        {
            public GuidId AppId { get; set; }

            public string Name { get; set; }

            public List<Guid> AuthorizedCircles { get; set; }

            public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }

            [JsonPropertyName("grant")]
            public KeyStore AppKeyStore { get; set; }

            public string CorsHostName { get; set; }

            public AppRegistration ToAppRegistration()
            {
                return new AppRegistration
                {
                    AppId = AppId,
                    Name = Name,
                    AuthorizedCircles = AuthorizedCircles,
                    CircleMemberPermissionGrant = CircleMemberPermissionGrant,
                    AppKeyStore = AppKeyStore,
                    CorsHostName = CorsHostName
                };
            }
        }
#nullable restore
    }
}
