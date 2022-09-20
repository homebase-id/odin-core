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

        public readonly GuidId DefaultCircleId = GuidId.FromString("default_circle");

        public CircleDefinitionService(ISystemStorage systemStorage, IDriveService driveService)
        {
            _driveService = driveService;
            _circleValueStorage = systemStorage.ThreeKeyValueStorage;
        }

        public Task<CircleDefinition> Create(CreateCircleRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();

            AssertValid(request.Permissions, request.DriveGrants?.ToList());

            var circle = new CircleDefinition()
            {
                Id = GuidId.NewId(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                Name = request.Name,
                Description = request.Description,
                DriveGrants = request.DriveGrants,
                Permissions = request.Permissions
            };

            _circleValueStorage.Upsert(circle.Id, GuidId.Empty, _circleDataType, circle);

            return Task.FromResult(circle);
        }

        public Task Update(CircleDefinition newCircleDefinition)
        {
            Guard.Argument(newCircleDefinition, nameof(newCircleDefinition)).NotNull();

            AssertValid(newCircleDefinition.Permissions, newCircleDefinition.DriveGrants?.ToList());

            var existingCircle = this.GetCircle(newCircleDefinition.Id);

            if (null == existingCircle)
            {
                throw new MissingDataException($"Invalid circle {newCircleDefinition.Id}");
            }
            
            existingCircle.LastUpdated = DateTimeExtensions.UnixTimeMilliseconds();
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

        public Task<IEnumerable<CircleDefinition>> GetCircles()
        {
            var circles = _circleValueStorage.GetByKey3<CircleDefinition>(_circleDataType);
            return Task.FromResult(circles);
        }

        public Task Delete(GuidId id)
        {
            var circle = GetCircle(id);

            if (null == circle)
            {
                throw new MissingDataException($"Invalid circle {id}");
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle

            _circleValueStorage.Delete(id);
            return Task.CompletedTask;
        }

        //

        private void AssertValid(PermissionSet permissionSet, List<DriveGrantRequest> driveGrantRequests)
        {
            bool hasDrives = driveGrantRequests?.Any() ?? false;
            bool hasPermissions = permissionSet?.Keys?.Any() ?? false;

            if (!hasPermissions && !hasDrives)
            {
                throw new YouverseException("A circle must grant at least one drive or one permission");
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
                throw new YouverseException("Invalid Permission key specified");
            }
        }

        private void AssertValidDriveGrants(IEnumerable<DriveGrantRequest> driveGrantRequests)
        {
            foreach (var dgr in driveGrantRequests)
            {
                //fail if the drive is invalid
                var drive = _driveService.GetDriveIdByAlias(dgr.PermissionedDrive.Drive, false).GetAwaiter().GetResult();
                if (drive == null)
                {
                    throw new YouverseException("Invalid drive specified on DriveGrantRequest");
                }
            }
        }
    }
}