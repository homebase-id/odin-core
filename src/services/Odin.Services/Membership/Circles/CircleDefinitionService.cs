using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
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

        public async Task<CircleDefinition> Create(CreateCircleRequest request, DatabaseConnection cn)
        {
            return await this.CreateCircleInternal(request, cn);
        }

        public async Task CreateSystemCircle(DatabaseConnection cn)
        {
            if (null == this.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId, cn))
            {
                await this.CreateCircleInternal(new CreateCircleRequest()
                {
                    Id = SystemCircleConstants.ConnectedIdentitiesSystemCircleId.Value,
                    Name = "All Connected Identities",
                    Description = "All Connected Identities",
                    DriveGrants = SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives,
                    Permissions = new PermissionSet()
                    {
                        Keys = new List<int>()
                    }
                }, cn, skipValidation: true);
            }
        }


        public async Task Update(CircleDefinition newCircleDefinition, DatabaseConnection cn)
        {
            await AssertValid(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList(), cn);

            var existingCircle = this.GetCircle(newCircleDefinition.Id, cn);

            if (null == existingCircle)
            {
                throw new OdinClientException($"Invalid circle {newCircleDefinition.Id}", OdinClientErrorCode.UnknownId);
            }

            existingCircle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            existingCircle.Description = newCircleDefinition.Description;
            existingCircle.Name = newCircleDefinition.Name;
            existingCircle.DriveGrants = newCircleDefinition.DriveGrants;
            existingCircle.Permissions = newCircleDefinition.Permissions;

            _circleValueStorage.Upsert(cn, existingCircle.Id, GuidId.Empty, _circleDataType, newCircleDefinition);
        }

        public bool IsEnabled(GuidId circleId, DatabaseConnection cn)
        {
            var circle = this.GetCircle(circleId, cn);
            return !circle?.Disabled ?? false;
        }

        public CircleDefinition GetCircle(GuidId circleId, DatabaseConnection cn)
        {
            var def = _circleValueStorage.Get<CircleDefinition>(cn, circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles(bool includeSystemCircle, DatabaseConnection cn)
        {
            var circles = _circleValueStorage.GetByCategory<CircleDefinition>(cn, _circleDataType);
            if (!includeSystemCircle)
            {
                return Task.FromResult(circles.Where(c => c.Id != SystemCircleConstants.ConnectedIdentitiesSystemCircleId.Value));
            }

            return Task.FromResult(circles);
        }

        public Task Delete(GuidId id, DatabaseConnection cn)
        {
            var circle = GetCircle(id, cn);

            if (null == circle)
            {
                throw new OdinClientException($"Invalid circle {id}", OdinClientErrorCode.UnknownId);
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle
            _circleValueStorage.Delete(cn, id);
            return Task.CompletedTask;
        }

        public async Task AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrantRequests, DatabaseConnection cn)
        {
            if (null == driveGrantRequests)
            {
                return;
            }

            foreach (var dgr in driveGrantRequests)
            {
                //fail if the drive is invalid
                var driveId = await _driveManager.GetDriveIdByAlias(dgr.PermissionedDrive.Drive, cn);

                if (driveId == null)
                {
                    throw new OdinClientException("Invalid drive specified on DriveGrantRequest", OdinClientErrorCode.InvalidGrantNonExistingDrive);
                }

                var drive = await _driveManager.GetDrive(driveId.GetValueOrDefault(), cn);

                //Allow access when OwnerOnly AND the only permission is Write; TODO: this defeats purpose of owneronly drive, i think
                if (drive.OwnerOnly && ((int)dgr.PermissionedDrive.Permission != (int)DrivePermission.Write))
                {
                    throw new OdinSecurityException("Cannot grant access to owner-only drives to circles");
                }
            }
        }

        //

        private async Task AssertValid(PermissionSet permissionSet, List<DriveGrantRequest> driveGrantRequests, DatabaseConnection cn)
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
                await AssertValidDriveGrants(driveGrantRequests, cn);
            }
        }

        private void AssertValidPermissionSet(PermissionSet permissionSet)
        {
            if (permissionSet.Keys.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)))
            {
                throw new OdinClientException("Invalid Permission key specified");
            }
        }

        private async Task<CircleDefinition> CreateCircleInternal(CreateCircleRequest request, DatabaseConnection cn, bool skipValidation = false)
        {
            if (!skipValidation)
            {
                await AssertValid(request.Permissions, request.DriveGrants?.ToList(), cn);
            }

            if (null != this.GetCircle(request.Id, cn))
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

            _circleValueStorage.Upsert(cn, circle.Id, GuidId.Empty, _circleDataType, circle);

            return circle;
        }
    }
}