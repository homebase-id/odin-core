using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Circle.Membership.Definition
{
    public class CircleDefinitionService
    {
        private readonly IDriveService _driveService;

        private readonly GuidId _circleDataType = GuidId.FromString("circle__");
        private readonly ThreeKeyValueStorage _circleValueStorage;

        public CircleDefinitionService(ITenantSystemStorage tenantSystemStorage, IDriveService driveService)
        {
            _driveService = driveService;
            _circleValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
        }

        public Task<CircleDefinition> Create(CreateCircleRequest request)
        {
            return this.CreateCircleInternal(request);
        }

        public void CreateSystemCircle()
        {
            if (null == this.GetCircle(CircleConstants.SystemCircleId))
            {
                this.CreateCircleInternal(new CreateCircleRequest()
                {
                    Id = CircleConstants.SystemCircleId.Value,
                    Name = "System Circle",
                    Description = "All Connected Identities",
                    DriveGrants = CircleConstants.InitialSystemCircleDrives,
                    Permissions = new PermissionSet()
                    {
                        Keys = new List<int>() { }
                    }
                }, skipValidation: true);
            }
        }


        public Task Update(CircleDefinition newCircleDefinition)
        {
            Guard.Argument(newCircleDefinition, nameof(newCircleDefinition)).NotNull();

            AssertValid(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList());

            var existingCircle = this.GetCircle(newCircleDefinition.Id);

            if (null == existingCircle)
            {
                throw new YouverseClientException($"Invalid circle {newCircleDefinition.Id}", YouverseClientErrorCode.UnknownId);
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
            Guard.Argument(circleId, nameof(circleId)).NotNull().Require(id => GuidId.IsValid(id));
            var def = _circleValueStorage.Get<CircleDefinition>(circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles(bool includeSystemCircle)
        {
            var circles = _circleValueStorage.GetByKey3<CircleDefinition>(_circleDataType);
            if (!includeSystemCircle)
            {
                return Task.FromResult(circles.Where(c => c.Id != CircleConstants.SystemCircleId.Value));
            }

            return Task.FromResult(circles);
        }

        public Task Delete(GuidId id)
        {
            var circle = GetCircle(id);

            if (null == circle)
            {
                throw new YouverseClientException($"Invalid circle {id}", YouverseClientErrorCode.UnknownId);
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle

            _circleValueStorage.Delete(id);
            return Task.CompletedTask;
        }

        public void AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrantRequests)
        {
            if(null == driveGrantRequests)
            {
                return;
            }

            foreach (var dgr in driveGrantRequests)
            {
                //fail if the drive is invalid
                var driveId = _driveService.GetDriveIdByAlias(dgr.PermissionedDrive.Drive, false).GetAwaiter().GetResult();

                if (driveId == null)
                {
                    throw new YouverseClientException("Invalid drive specified on DriveGrantRequest", YouverseClientErrorCode.InvalidGrantNonExistingDrive);
                }

                var drive = _driveService.GetDrive(driveId.GetValueOrDefault()).GetAwaiter().GetResult();

                if (drive.OwnerOnly)
                {
                    throw new YouverseSecurityException("Cannot grant access to owner-only drives to circles");
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
                throw new YouverseClientException("A circle must grant at least one drive or one permission", YouverseClientErrorCode.AtLeastOneDriveOrPermissionRequiredForCircle);
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
                throw new YouverseClientException("Invalid Permission key specified");
            }
        }

        private Task<CircleDefinition> CreateCircleInternal(CreateCircleRequest request, bool skipValidation = false)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            Guard.Argument(request.Id, nameof(request.Id)).Require(id => id != Guid.Empty);

            if (!skipValidation)
            {
                AssertValid(request.Permissions, request.DriveGrants?.ToList());
            }

            if (null != this.GetCircle(request.Id))
            {
                throw new YouverseClientException("Circle with Id already exists", YouverseClientErrorCode.IdAlreadyExists);
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
