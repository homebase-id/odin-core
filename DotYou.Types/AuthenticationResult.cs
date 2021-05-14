using System;

namespace DotYou.Types
{
    public class AuthenticationResult
    {
        public Guid Token { get; set; }

        public DotYouIdentity DotYouId { get; set; }
    }
}