using System;

namespace DotYou.Types
{
    public class AuthenticationResult
    {
        public Guid Token { get; set; }

        public DotYouIdentity DotYouId { get; set; }
        
        /// <summary>
        /// The Client's 1/2 of the KeK
        /// </summary>
        public Guid Token2 { get; set; }
    }
}