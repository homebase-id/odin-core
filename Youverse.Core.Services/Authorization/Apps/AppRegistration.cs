using System;
using System.Collections.Generic;
using LiteDB;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration()
        {
        }

        [BsonId]
        public ByteArrayId AppId { get; set; }
        
        public string Name { get; set; }

        public ExchangeGrant Grant { get; set; }
        
    }
}