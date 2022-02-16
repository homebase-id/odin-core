using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authentication;

namespace Youverse.Hosting.Tests
{
    public class OwnerAuthTokenContext
    {
        public DotYouAuthenticationResult AuthResult { get; set; }
        public SensitiveByteArray SharedSecret { get; set; }
    }
}