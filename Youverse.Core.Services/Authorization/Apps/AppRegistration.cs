
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration()
        {
        }

        public ByteArrayId AppId { get; set; }

        public string Name { get; set; }

        public ExchangeGrant Grant { get; set; }

        public RedactedAppRegistration Redacted()
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new RedactedAppRegistration()
            {
                AppId = this.AppId,
                Name = this.Name,
                IsRevoked = this.Grant.IsRevoked,
                Created = this.Grant.Created,
                Modified = this.Grant.Modified,
                Grant = this.Grant.Redacted()
            };
        }
    }
}