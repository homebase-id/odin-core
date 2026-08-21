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

            existingCircle.AppId = newCircleDefinition.AppId;
            existingCircle.GrantOn = newCircleDefinition.GrantOn;
            existingCircle.Designation = newCircleDefinition.Designation;
            existingCircle.Emoji = newCircleDefinition.Emoji;

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
                Permissions = request.Permissions
            };

            await db.CircleCached.UpsertAsync(ToRecord(circle));

            return circle;
        }

        //

        internal static CircleRecord ToRecord(CircleDefinition definition)
        {
            return new CircleRecord
            {
                circleId = definition.Id,
                circleName = definition.Name,
                data = OdinSystemSerializer.Serialize(definition).ToUtf8ByteArray(),

                // Columns, not blob fields -- see the class remarks.
                AppId = definition.AppId,
                GrantOn = (int)definition.GrantOn,
                Designation = (int)definition.Designation,
                Emoji = definition.Emoji
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