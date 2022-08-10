using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Serialization;
using Youverse.Core.SystemStorage;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.Services.Contacts.Circle.Definition
{
    public class CircleDefinitionService
    {
        private readonly byte[] _circleDataType = "circle".ToLower().ToUtf8ByteArray();

        private readonly TableKeyThreeValue _circleStorage;

        public CircleDefinitionService(ISystemStorage systemStorage)
        {
            _circleStorage = systemStorage.KeyValueStorage.ThreeKeyStorage;
        }

        public Task Create(CreateCircleRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();

            var id = Guid.NewGuid();

            var circle = new CircleDefinition()
            {
                Id = id,
                Created = DateTimeExtensions.UnixTimeMilliseconds(),
                Name = request.Name,
                Description = request.Description,
                Drives = request.Drives,
                Permissions = request.Permissions
            };

            var json = DotYouSystemSerializer.Serialize(circle);
            _circleStorage.UpsertRow(circle.Id.ToByteArray(), Array.Empty<byte>(), _circleDataType, json.ToUtf8ByteArray());

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

            var json = DotYouSystemSerializer.Serialize(newCircleDefinition);
            _circleStorage.UpsertRow(existingCircle.Id.ToByteArray(), Array.Empty<byte>(), _circleDataType, json.ToUtf8ByteArray());

            return Task.CompletedTask;
        }

        public CircleDefinition GetCircle(Guid circleId)
        {
            var bytes = _circleStorage.Get(circleId.ToByteArray());
            if (null == bytes)
            {
                return null;
            }

            return FromBytes(bytes);
        }

        public Task<IEnumerable<CircleDefinition>> GetCircles()
        {
            var list = _circleStorage.GetByKeyThree(_circleDataType);
            var circles = list.Select(FromBytes);
            return Task.FromResult(circles);
        }

        public Task Delete(Guid id)
        {
            var circle = GetCircle(id);

            if (null == circle)
            {
                throw new MissingDataException($"Invalid circle {id}");
            }

            //TODO: update the circle.Permissions and circle.Drives for all members of the circle


            _circleStorage.DeleteRow(id.ToByteArray());
            return Task.CompletedTask;
        }

        //

        private CircleDefinition FromBytes(byte[] bytes)
        {
            return DotYouSystemSerializer.Deserialize<CircleDefinition>(bytes.ToStringFromUtf8Bytes());
        }
    }
}