using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Hosting.Tests
{
    public class OwnerAuthTokenContext
    {
        public ClientAuthenticationToken AuthenticationResult { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }
    }
}