using System;
using LiteDB;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration() { }

        [BsonId]
        public Guid AppId { get; set; }
        
        public string Name { get; set; }

        public IExchangeGrant Grant { get; set; }
    }
}