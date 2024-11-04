using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Membership.Circles
{
    public class CircleDefinitionService
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly DriveManager _driveManager;

        private readonly byte[] _circleDataType = Guid.Parse("2a915ab8-412e-42d8-b157-a123f107f224").ToByteArray();
        private readonly ThreeKeyValueStorage _circleValueStorage;

        public CircleDefinitionService(TenantSystemStorage tenantSystemStorage, DriveManager driveManager)
        {
            _tenantSystemStorage = tenantSystemStorage;
            _driveManager = driveManager;
            const string circleValueContextKey = "dc1c198c-c280-4b9c-93ce-d417d0a58491";
            _circleValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(circleValueContextKey));
        }

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
                    await this.UpdateAsync(SystemCircleConstants.ConfirmedConnectionsDefinition);
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
                    await this.UpdateAsync(SystemCircleConstants.AutoConnectionsSystemCircleDefinition);
                }
            }
        }

        public async Task UpdateAsync(CircleDefinition newCircleDefinition)
        {
            await AssertValidAsync(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList());

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

            await _circleValueStorage.UpsertAsync(_tenantSystemStorage.IdentityDatabase, existingCircle.Id, GuidId.Empty, _circleDataType, newCircleDefinition);
        }

        public async Task<bool> IsEnabledAsync(GuidId circleId)
        {
            var circle = await GetCircleAsync(circleId);
            return !circle?.Disabled ?? false;
        }

        public async Task<CircleDefinition> GetCircleAsync(GuidId circleId)
        {
            var def = await _circleValueStorage.GetAsync<CircleDefinition>(_tenantSystemStorage.IdentityDatabase, circleId);
            return def;
        }
        
        public async Task<List<CircleDefinition>> GetCirclesAsync(bool includeSystemCircle)
        {
            var circles = (await _circleValueStorage.GetByCategoryAsync<CircleDefinition>(_tenantSystemStorage.IdentityDatabase, _circleDataType) ?? []).ToList();
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
            await _circleValueStorage.DeleteAsync(_tenantSystemStorage.IdentityDatabase, id);
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
                var driveId = await _driveManager.GetDriveIdByAliasAsync(dgr.PermissionedDrive.Drive, _tenantSystemStorage.IdentityDatabase);

                if (driveId == null)
                {
                    throw new OdinClientException("Invalid drive specified on DriveGrantRequest", OdinClientErrorCode.InvalidGrantNonExistingDrive);
                }

                var drive = await _driveManager.GetDriveAsync(driveId.GetValueOrDefault(), _tenantSystemStorage.IdentityDatabase);

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

            await _circleValueStorage.UpsertAsync(_tenantSystemStorage.IdentityDatabase, circle.Id, GuidId.Empty, _circleDataType, circle);

            return circle;
        }
    }
}