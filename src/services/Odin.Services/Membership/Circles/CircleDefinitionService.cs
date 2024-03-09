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
        private readonly DriveManager _driveManager;

        private readonly byte[] _circleDataType = Guid.Parse("2a915ab8-412e-42d8-b157-a123f107f224").ToByteArray();
        private readonly ThreeKeyValueStorage _circleValueStorage;

        public CircleDefinitionService(TenantSystemStorage tenantSystemStorage, DriveManager driveManager)
        {
            _driveManager = driveManager;
            const string circleValueContextKey = "dc1c198c-c280-4b9c-93ce-d417d0a58491";
            _circleValueStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(circleValueContextKey));
        }

        public Task<CircleDefinition> Create(CreateCircleRequest request)
        {
            return this.CreateCircleInternal(request);
        }

        public void CreateSystemCircle()
        {
            if (null == this.GetCircle(SystemCircleConstants.ConnectedIdentitiesSystemCircleId))
            {
                this.CreateCircleInternal(new CreateCircleRequest()
                {
                    Id = SystemCircleConstants.ConnectedIdentitiesSystemCircleId.Value,
                    Name = "All Connected Identities",
                    Description = "All Connected Identities",
                    DriveGrants = SystemCircleConstants.ConnectedIdentitiesSystemCircleInitialDrives,
                    Permissions = new PermissionSet()
                    {
                        Keys = new List<int>()
                    }
                }, skipValidation: true);
            }
        }


        public Task Update(CircleDefinition newCircleDefinition)
        {
            AssertValid(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList());

            var existingCircle = this.GetCircle(newCircleDefinition.Id);

            if (null == existingCircle)
            {
                throw new OdinClientException($"Invalid circle {newCircleDefinition.Id}", OdinClientErrorCode.UnknownId);
            }

            existingCircle.LastUpdated = UnixTimeUtc.Now().milliseconds;
            existingCircle.Description = newCircleDefinition.Description;
            existingCircle.Name = newCircleDefinition.Name;
            existingCircle.DriveGrants = newCircleDefinition.DriveGrants;
            existingCircle.Permissions = newCircleDefinition.Permissions;

            _circleValueStorage.Upsert(existingCircle.Id, GuidId.Empty, _circleDataType, newCircleDefinition);

            return Task.CompletedTask;
        }

        public bool IsEnabled(GuidId circleId)
        {
            var circle = this.GetCircle(circleId);
            return !circle?.Disabled ?? false;
        }

        public CircleDefinition GetCircle(GuidId circleId)
        {
            var def = _circleValueStorage.Get<CircleDefinition>(circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles(bool includeSystemCircle)
        {
            var circles = _circleValueStorage.GetByCategory<CircleDefinition>(_circleDataType);
            if (!includeSystemCircle)
            {
                return Task.FromResult(circles.Where(c => c.Id != SystemCircleConstants.ConnectedIdentitiesSystemCircleId.Value));
            }

            return Task.FromResult(circles);
        }

        public Task Delete(GuidId id)
        {
            var circle = GetCircle(id);

            if (null == circle)
            {
                throw new OdinClientException($"Invalid circle {id}", OdinClientErrorCode.UnknownId);
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle

            _circleValueStorage.Delete(id);
            return Task.CompletedTask;
        }

        public void AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrantRequests)
        {
            if (null == driveGrantRequests)
            {
                return;
            }

            foreach (var dgr in driveGrantRequests)
            {
                //fail if the drive is invalid
                var driveId = _driveManager.GetDriveIdByAlias(dgr.PermissionedDrive.Drive, false).GetAwaiter().GetResult();

                if (driveId == null)
                {
                    throw new OdinClientException("Invalid drive specified on DriveGrantRequest", OdinClientErrorCode.InvalidGrantNonExistingDrive);
                }

                var drive = _driveManager.GetDrive(driveId.GetValueOrDefault()).GetAwaiter().GetResult();

                //Allow access when OwnerOnly AND the only permission is Write; TODO: this defeats purpose of owneronly drive, i think
                if (drive.OwnerOnly && ((int)dgr.PermissionedDrive.Permission != (int)DrivePermission.Write))
                {
                    throw new OdinSecurityException("Cannot grant access to owner-only drives to circles");
                }
            }
        }

        //

        private void AssertValid(PermissionSet permissionSet, List<DriveGrantRequest> driveGrantRequests)
        {
            bool hasDrives = driveGrantRequests?.Any() ?? false;
            bool hasPermissions = permissionSet?.Keys?.Any() ?? false;

            if (!hasPermissions && !hasDrives)
            {
                throw new OdinClientException("A circle must grant at least one drive or one permission", OdinClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);
            }

            if (hasPermissions)
            {
                AssertValidPermissionSet(permissionSet);
            }

            if (hasDrives)
            {
                AssertValidDriveGrants(driveGrantRequests);
            }
        }

        private void AssertValidPermissionSet(PermissionSet permissionSet)
        {
            if (permissionSet.Keys.Any(k => !PermissionKeyAllowance.IsValidCirclePermission(k)))
            {
                throw new OdinClientException("Invalid Permission key specified");
            }
        }

        private Task<CircleDefinition> CreateCircleInternal(CreateCircleRequest request, bool skipValidation = false)
        {
            if (!skipValidation)
            {
                AssertValid(request.Permissions, request.DriveGrants?.ToList());
            }

            if (null != this.GetCircle(request.Id))
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

            _circleValueStorage.Upsert(circle.Id, GuidId.Empty, _circleDataType, circle);

            return Task.FromResult(circle);
        }
    }
}