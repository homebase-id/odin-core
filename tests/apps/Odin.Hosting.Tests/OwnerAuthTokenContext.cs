using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Tests
{
    public class OwnerAuthTokenContext
    {
        public ClientAuthenticationToken AuthenticationToken { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }
    }
}