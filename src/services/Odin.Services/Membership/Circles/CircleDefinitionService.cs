using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Membership.Circles
{
    /// <summary>
    /// Circle definitions live in the <c>Circle</c> table, one row per circle.
    /// </summary>
    /// <remarks>
    /// They used to live in the shared key-three-value blob, where <c>AppId</c> and <c>GrantOn</c> could
    /// not be queried or constrained at all.  The enrollment pipeline has to ask "which circles enrol on
    /// connect?" on the hot path, which is a <c>WHERE GrantOn = ?</c> against an indexed column, not a
    /// load-all-and-deserialize.  <see cref="CircleDefinitionMigrationService"/> copies existing rows
    /// across.
    /// <para>
    /// Those four fields are columns and are excluded from the row's <c>data</c> blob, so a query on the
    /// column can never disagree with the hydrated object.
    /// </para>
    /// </remarks>
    public class CircleDefinitionService(IDriveManager driveManager, IdentityDatabase db)
    {

        public async Task<CircleDefinition> CreateAsync(CreateCircleRequest request)
        {
            return await this.CreateCircleInternalAsync(request);
        }

        /// <summary>
        /// Creates an app's declared circle, or updates it if the app has declared it before.
        /// </summary>
        /// <remarks>
        /// Matched on circle id, so replaying a registration updates the circle rather than duplicating
        /// it.  An app may only touch circles it owns: a circle with a different <c>AppId</c> -- another
        /// app's, or an owner circle -- is refused rather than taken over.
        /// </remarks>
        public async Task<CircleDefinition> CreateOrUpdateAppCircleAsync(Guid appId, CreateCircleRequest request)
        {
            request.AppId = appId;

            var existing = await GetCircleAsync(request.Id);

            if (existing == null)
            {
                return await CreateCircleInternalAsync(request);
            }

            if (existing.AppId != appId)
            {
                throw new OdinClientException(
                    $"Circle {request.Id} is not owned by app {appId} and cannot be redefined by it",
                    OdinClientErrorCode.CircleNotOwnedByApp);
            }

            await UpdateAsync(new CircleDefinition
            {
                Id = existing.Id,
                Created = existing.Created,
                Name = request.Name,
                Description = request.Description,
                DriveGrants = request.DriveGrants,
                Permissions = request.Permissions,
                AppId = appId,
                GrantOn = request.GrantOn,
                Designation = request.Designation,
                Emoji = request.Emoji
            });

            return await GetCircleAsync(request.Id);
        }

        public async Task EnsureSystemCirclesExistAsync()
        {
            var confirmedCircleDefinition = await GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId);
            if (null == confirmedCircleDefinition)
            {
                var def = SystemCircleConstants.ConfirmedConnectionsDefinition;
                await this.CreateCircleInternalAsync(new CreateCircleRequest
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    DriveGrants = def.DriveGrants,
                    Permissions = def.Permissions
                }, skipValidation: true);
            }
            else
            {
                if (SystemCircleConstants.ConfirmedConnectionsDefinition != confirmedCircleDefinition)
                {
                    // System circle definitions are trusted constants whose drive grants reference system
                    // drives guaranteed to exist via EnsureSystemDrivesExist. Skip validation so the reconcile
                    // doesn't deadlock during initial setup, where circles are created before system drives.
                    await this.UpdateAsync(SystemCircleConstants.ConfirmedConnectionsDefinition, skipValidation: true);
                }
            }

            var autoCircleDef = await GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId);
            if (null == autoCircleDef)
            {
                var def = SystemCircleConstants.AutoConnectionsSystemCircleDefinition;
                await CreateCircleInternalAsync(new CreateCircleRequest
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    DriveGrants = def.DriveGrants,
                    Permissions = def.Permissions
                }, skipValidation: true);
            }
            else
            {
                if (SystemCircleConstants.AutoConnectionsSystemCircleDefinition != autoCircleDef)
                {
                    await this.UpdateAsync(SystemCircleConstants.AutoConnectionsSystemCircleDefinition, skipValidation: true);
                }
            }

            await EnsureBuiltInCirclesExistAsync();
        }

        /// <summary>
        /// Provisions the built-in circles that ship with every identity. Unlike system circles, these
        /// behave as normal owner-managed circles once created (see <see cref="BuiltInCircleConstants"/>).
        /// </summary>
        public async Task EnsureBuiltInCirclesExistAsync()
        {
            var emergencyLocationAccessDef = await GetCircleAsync(BuiltInCircleConstants.EmergencyLocationAccessCircleId);
            if (null == emergencyLocationAccessDef)
            {
                var def = BuiltInCircleConstants.EmergencyLocationAccessDefinition;
                await CreateCircleInternalAsync(new CreateCircleRequest
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    DriveGrants = def.DriveGrants,
                    Permissions = def.Permissions
                }, skipValidation: true);
            }
            else
            {
                if (BuiltInCircleConstants.EmergencyLocationAccessDefinition != emergencyLocationAccessDef)
                {
                    await this.UpdateAsync(BuiltInCircleConstants.EmergencyLocationAccessDefinition, skipValidation: true);
                }
            }
        }

        public async Task UpdateAsync(CircleDefinition newCircleDefinition, bool skipValidation = false)
        {
            if (!skipValidation)
            {
                await AssertValidAsync(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList());
            }

            var existingCircle = await GetCircleAsync(newCircleDefinition.Id);

            if (null == existingCircle)
            {
                throw new OdinClientException($"Invalid circle {newCircleDefinition.Id}", OdinClientErrorCode.UnknownId);
            }

            existingCircle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            existingCircle.Description = newCircleDefinition.Description;
            existingCircle.Name = newCircleDefinition.Name;
            existingCircle.DriveGrants = newCircleDefinition.DriveGrants;
            existingCircle.Permissions = newCircleDefinition.Permissions;

            // AppId deliberately not taken from the request: ownership is set when the circle is
            // created and must not be reassignable by anyone who can PUT a definition.
            existingCircle.GrantOn = newCircleDefinition.GrantOn;
            existingCircle.Designation = newCircleDefinition.Designation;
            existingCircle.Emoji = newCircleDefinition.Emoji;

            // Re-checked on every write, not just the first: the invariant has to hold whenever GrantOn
            // changes, and an update is the way a circle becomes ambient.
            await AssertDepositOnlyIfAmbientAsync(existingCircle);

            await db.CircleCached.UpsertAsync(ToRecord(existingCircle));
        }

        public async Task<bool> IsEnabledAsync(GuidId circleId)
        {
            var circle = await GetCircleAsync(circleId);
            return !circle?.Disabled ?? false;
        }

        public async Task<CircleDefinition> GetCircleAsync(GuidId circleId)
        {
            var record = await db.CircleCached.GetAsync(circleId);
            return record == null ? null : FromRecord(record);
        }

        /// <summary>
        /// Circles whose owning app wants members enrolled at the given moment.  Served straight from
        /// the indexed column -- this is the auto-connect hot path.
        /// </summary>
        public async Task<List<CircleDefinition>> GetCirclesByGrantOnAsync(CircleGrantOn grantOn)
        {
            var records = await db.CircleCached.GetByGrantOnAsync((int)grantOn);
            return records.Select(FromRecord).ToList();
        }

        public async Task<List<CircleDefinition>> GetCirclesAsync(bool includeSystemCircle)
        {
            var circles = (await db.CircleCached.GetAllAsync()).Select(FromRecord).ToList();
            if (!includeSystemCircle)
            {
                circles.RemoveAll(def => SystemCircleConstants.AllSystemCircles.Exists(sc => sc == def.Id));
            }

            return circles;
        }

        public async Task DeleteAsync(GuidId id)
        {
            var circle = await GetCircleAsync(id);

            if (null == circle)
            {
                throw new OdinClientException($"Invalid circle {id}", OdinClientErrorCode.UnknownId);
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle
            await db.CircleCached.DeleteAsync(id);
        }

        public async Task AssertValidDriveGrantsAsync(IEnumerable<DriveGrantRequest> driveGrantRequests)
        {
            if (null == driveGrantRequests)
            {
                return;
            }

            foreach (var dgr in driveGrantRequests)
            {
                //fail if the drive is invalid
                var driveId = dgr.PermissionedDrive.Drive.Alias;

                if (driveId == null)
                {
                    throw new OdinClientException("Invalid drive specified on DriveGrantRequest", OdinClientErrorCode.InvalidGrantNonExistingDrive);
                }

                var drive = await driveManager.GetDriveAsync(driveId);

                if (drive == null)
                {
                    throw new OdinClientException(
                        $"DriveGrantRequest references non-existent drive {dgr.PermissionedDrive.Drive}",
                        OdinClientErrorCode.InvalidGrantNonExistingDrive);
                }

                //Allow access when OwnerOnly AND the only permission is Write or React; TODO: this defeats purpose of owneronly drive, i think
                var hasValidPermission = dgr.PermissionedDrive.Permission.HasFlag(DrivePermission.Write) ||
                                         dgr.PermissionedDrive.Permission.HasFlag(DrivePermission.React);
                if (drive.OwnerOnly && !hasValidPermission)
                {
                    throw new OdinSecurityException("Cannot grant access to owner-only drives to circles");
                }
            }
        }

        //

        private async Task AssertValidAsync(PermissionSet permissionSet, List<DriveGrantRequest> driveGrantRequests)
        {
            bool hasDrives = driveGrantRequests?.Any() ?? false;
            bool hasPermissions = permissionSet?.Keys?.Any() ?? false;

            if (!hasPermissions && !hasDrives)
            {
                throw new OdinClientException("A circle must grant at least one drive or one permission",
                    OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);
            }

            if (hasPermissions)
            {
                AssertValidPermissionSet(permissionSet);
            }

            if (hasDrives)
            {
                await AssertValidDriveGrantsAsync(driveGrantRequests);
            }
        }

        /// <summary>
        /// A circle that enrols without the owner present may hand out deposit capability and nothing
        /// else: write/react drive permissions, no read beyond drives that are already public, and no
        /// permission keys.
        /// </summary>
        /// <remarks>
        /// This is what makes "an unreviewed connection holds zero read keys" an enforced property rather
        /// than a convention.  It is checked when the definition is written -- not when the grant is
        /// minted -- for the same confused-deputy reason the drives-it-can-already-read rule is: an app
        /// can plant a definition, and the next owner-driven grant would mint it with the master key in
        /// scope.
        /// <para>
        /// Read is permitted on drives with <c>AllowAnonymousReads</c>, since a member gains nothing a
        /// stranger did not already have.  That carve-out is how connections keep decrypting public-drive
        /// content.
        /// </para>
        /// </remarks>
        public async Task AssertDepositOnlyIfAmbientAsync(CircleDefinition circle)
        {
            if (circle.GrantOn is not (CircleGrantOn.Connect or CircleGrantOn.OwnFlowConnect))
            {
                return;
            }

            if (circle.Permissions?.Keys?.Any() ?? false)
            {
                throw new OdinClientException(
                    $"Circle '{circle.Name}' enrols on connect and cannot carry permission keys; " +
                    "identity-wide keys are only mintable at the review.",
                    OdinClientErrorCode.CannotGrantKeysOnAmbientCircle);
            }

            foreach (var grant in circle.DriveGrants ?? [])
            {
                var permission = grant.PermissionedDrive.Permission;

                if (!permission.HasFlag(DrivePermission.Read))
                {
                    continue;
                }

                var drive = await driveManager.GetDriveAsync(grant.PermissionedDrive.Drive.Alias);

                if (drive is not { AllowAnonymousReads: true })
                {
                    throw new OdinClientException(
                        $"Circle '{circle.Name}' enrols on connect and cannot grant read on " +
                        $"{grant.PermissionedDrive.Drive}; read grants carry a storage key and are only " +
                        "mintable at the review.",
                        OdinClientErrorCode.CannotGrantReadOnAmbientCircle);
                }
            }
        }

        private void AssertValidPermissionSet(PermissionSet permissionSet)
        {
            if (permissionSet.Keys.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)))
            {
                throw new OdinClientException("Invalid Permission key specified");
            }
        }

        private async Task<CircleDefinition> CreateCircleInternalAsync(CreateCircleRequest request, bool skipValidation = false)
        {
            if (!skipValidation)
            {
                await AssertValidAsync(request.Permissions, request.DriveGrants?.ToList());
            }

            if (null != await GetCircleAsync(request.Id))
            {
                throw new OdinClientException("Circle with Id already exists", OdinClientErrorCode.IdAlreadyExists);
            }

            var now = UnixTimeUtc.Now().milliseconds;
            var circle = new CircleDefinition()
            {
                Id = request.Id,
                Created = now,
                LastUpdated = now,
                Name = request.Name,
                Description = request.Description,
                DriveGrants = request.DriveGrants,
                Permissions = request.Permissions,
                AppId = request.AppId,
                GrantOn = request.GrantOn,
                Designation = request.Designation,
                Emoji = request.Emoji
            };

            await AssertDepositOnlyIfAmbientAsync(circle);

            await db.CircleCached.UpsertAsync(ToRecord(circle));

            return circle;
        }

        //

        internal static CircleRecord ToRecord(CircleDefinition definition)
        {
            var appId = definition.AppId;
            var grantOn = definition.GrantOn;
            var designation = definition.Designation;
            var emoji = definition.Emoji;

            // Clear before serializing so the blob holds no second copy of what the columns own -- the
            // same trick ToConnectionsRecord uses for the grant collections. Restored immediately: the
            // caller's object is still live.
            definition.AppId = null;
            definition.GrantOn = CircleGrantOn.None;
            definition.Designation = CircleDesignation.Personal;
            definition.Emoji = null;

            byte[] data;
            try
            {
                data = OdinSystemSerializer.Serialize(definition).ToUtf8ByteArray();
            }
            finally
            {
                definition.AppId = appId;
                definition.GrantOn = grantOn;
                definition.Designation = designation;
                definition.Emoji = emoji;
            }

            return new CircleRecord
            {
                circleId = definition.Id,
                circleName = definition.Name,
                data = data,
                AppId = appId,
                GrantOn = (int)grantOn,
                Designation = (int)designation,
                Emoji = emoji
            };
        }

        internal static CircleDefinition FromRecord(CircleRecord record)
        {
            var definition = OdinSystemSerializer.Deserialize<CircleDefinition>(record.data.ToStringFromUtf8Bytes());

            definition.AppId = record.AppId;
            definition.GrantOn = (CircleGrantOn)record.GrantOn;
            definition.Designation = (CircleDesignation)record.Designation;
            definition.Emoji = record.Emoji;

            return definition;
        }
    }
}