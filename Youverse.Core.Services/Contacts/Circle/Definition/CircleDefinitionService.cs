using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Contacts.Circle.Definition
{
    public class CircleDefinitionService
    {
        private readonly ByteArrayId _circleDataType = ByteArrayId.FromString("circle__");
        private readonly ThreeKeyValueStorage _circleValueStorage;

        public readonly ByteArrayId DefaultCircleId = ByteArrayId.FromString("default_circle");

        // public void CreateInitialDefaultCircle()
        // {
        //     var defCircle = this.GetCircle(this.DefaultCircleId);
        //     if (null == defCircle)
        //     {
        //         this.Create(new CreateCircleRequest()
        //         {
        //             Name = "System Circle",
        //             Description = "Default Circle",
        //             Drives = new List<DriveGrantRequest>() { },
        //             Permissions = new PermissionSet(CirclePermissionFlags.None)
        //         });
        //     }
        // }

        public CircleDefinitionService(ISystemStorage systemStorage)
        {
            _circleValueStorage = systemStorage.ThreeKeyValueStorage;
        }

        public Task Create(CreateCircleRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();

            var circle = new CircleDefinition()
            {
                Id = ByteArrayId.NewId(),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                Name = request.Name,
                Description = request.Description,
                DrivesGrants = request.Drives,
                Permissions = request.Permissions
            };

            _circleValueStorage.Upsert(circle.Id, ByteArrayId.Empty.Value, _circleDataType.Value, circle);

            return Task.CompletedTask;
        }

        public Task Update(CircleDefinition newCircleDefinition)
        {
            Guard.Argument(newCircleDefinition, nameof(newCircleDefinition)).NotNull();

            var existingCircle = this.GetCircle(newCircleDefinition.Id);

            if (null == existingCircle)
            {
                throw new MissingDataException($"Invalid circle {newCircleDefinition.Id}");
            }

            var permissionChanges = newCircleDefinition.Permissions != existingCircle.Permissions;
            //var driveChanges = (newCircleDefinition.Drives?.Count() ?? 0) != (existingCircle.Drives?.Count() ?? 0);
            bool driveChanges = (existingCircle.DrivesGrants != null && newCircleDefinition.DrivesGrants != null) &&
                                (newCircleDefinition.DrivesGrants.Except(existingCircle.DrivesGrants)).Any();

            //TODO: apply new permissions to all circle members
            if (permissionChanges || driveChanges)
            {
                //TODO: for each member of this circle, update their permissions
            }

            existingCircle.LastUpdated = DateTimeExtensions.UnixTimeMilliseconds();
            existingCircle.Description = newCircleDefinition.Description;
            existingCircle.Name = newCircleDefinition.Name;
            existingCircle.DrivesGrants = newCircleDefinition.DrivesGrants;
            existingCircle.Permissions = newCircleDefinition.Permissions;

            _circleValueStorage.Upsert(existingCircle.Id, ByteArrayId.Empty.Value, _circleDataType.Value, newCircleDefinition);

            return Task.CompletedTask;
        }

        public CircleDefinition GetCircle(ByteArrayId circleId)
        {
            Guard.Argument(circleId, nameof(circleId)).NotNull().Require(id => ByteArrayId.IsValid(id));
            var def = _circleValueStorage.Get<CircleDefinition>(circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles()
        {
            var circles = _circleValueStorage.GetByKey3<CircleDefinition>(_circleDataType);
            return Task.FromResult(circles);
        }

        public Task Delete(ByteArrayId id)
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
    }
}