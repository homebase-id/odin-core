using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Contacts.Circle.Definition
{
    public class CircleDefinitionService
    {
        private readonly ByteArrayId _circleDataType = ByteArrayId.FromString("circle__");

        private readonly ThreeKeyStorage _circleStorage;

        public CircleDefinitionService(ISystemStorage systemStorage)
        {
            _circleStorage = systemStorage.KeyValueStorage.ThreeKeyStorage2;
        }

        public Task Create(CreateCircleRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            
            var circle = new CircleDefinition()
            {
                Id = new ByteArrayId( Guid.NewGuid().ToByteArray()),
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                Name = request.Name,
                Description = request.Description,
                Drives = request.Drives,
                Permissions = request.Permissions
            };

            _circleStorage.Upsert(circle.Id.Value, ByteArrayId.Empty.Value, _circleDataType.Value, circle);

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
            bool driveChanges = (existingCircle.Drives != null && newCircleDefinition.Drives != null) &&
                                (newCircleDefinition.Drives.Except(existingCircle.Drives)).Any();

            //TODO: apply new permissions to all circle members
            if (permissionChanges || driveChanges)
            {
                //TODO: for each member of this circle, update their permissions
            }

            existingCircle.LastUpdated = DateTimeExtensions.UnixTimeMilliseconds();
            existingCircle.Description = newCircleDefinition.Description;
            existingCircle.Name = newCircleDefinition.Name;
            existingCircle.Drives = newCircleDefinition.Drives;
            existingCircle.Permissions = newCircleDefinition.Permissions;

            _circleStorage.Upsert(existingCircle.Id.Value, ByteArrayId.Empty.Value, _circleDataType.Value, newCircleDefinition);

            return Task.CompletedTask;
        }

        public CircleDefinition GetCircle(ByteArrayId circleId)
        {
            var def = _circleStorage.Get<CircleDefinition>(circleId);
            return def;
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles()
        {
            var circles = _circleStorage.GetByKey3<CircleDefinition>(_circleDataType);
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

            _circleStorage.Delete(id.Value);
            return Task.CompletedTask;
        }

        //
    }
}