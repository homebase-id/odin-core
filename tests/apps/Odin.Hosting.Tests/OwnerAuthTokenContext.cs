using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests
{
    public class OwnerAuthTokenContext
    {
        public ClientAuthenticationToken AuthenticationResult { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }
    }
}