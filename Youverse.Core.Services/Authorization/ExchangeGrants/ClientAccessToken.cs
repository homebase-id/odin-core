using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class ClientAccessToken
    {
        public Guid Id { get; set; }
        public SensitiveByteArray AccessTokenHalfKey { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }

        public ClientAuthenticationToken ToAuthenticationToken()
        {
            ClientAuthenticationToken authenticationToken = new ClientAuthenticationToken()
            {
                Id = this.Id,
                AccessTokenHalfKey = this.AccessTokenHalfKey
            };
            return authenticationToken;
        }

        public void Wipe()
        {
            this.AccessTokenHalfKey?.Wipe();
            this.SharedSecret?.Wipe();
        }
    }
}