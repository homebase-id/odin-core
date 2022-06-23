using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Newtonsoft.Json;
using Youverse.Core.Services.Base;
using Youverse.Core.SystemStorage;
using Youverse.Core.SystemStorage.SqliteKeyValue;

namespace Youverse.Core.Services.Contacts.Circle.Membership
{
    public class CircleCreationRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        
    }
    public class CircleDefinitionService : ICircleDefinitionService
    {
        private readonly byte[] _circleDataType = "circle".ToLower().ToUtf8ByteArray();
        
        private readonly TableKeyThreeValue _circleStorage;
        
        public CircleDefinitionService(ISystemStorage systemStorage)
        {
            _circleStorage = systemStorage.KeyValueStorage.ThreeKeyStorage;
        }
        
        public Circle GetRootCircle()
        {
            var defaultCircle = new Circle()
            {
                Id = Guid.Parse("11111118-0000-0000-0001-000000008733"),
                Name = "Anonymous",
            };

            return defaultCircle;
        }


        public Task CreateCircle(CircleCreationRequest request)
        {
            Guard.Argument(request, nameof(request)).NotNull();
            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();

            var id = Guid.NewGuid();

            var circle = new Circle()
            {
                Id = id,
                Name = request.Name,
                Description = request.Description
                
                //TODO: handle circle access
            };
            
            var json = JsonConvert.SerializeObject(circle);
            _circleStorage.UpsertRow(circle.Id.ToByteArray(), null, _circleDataType, json.ToUtf8ByteArray());

            return Task.CompletedTask;
        }
    }
}