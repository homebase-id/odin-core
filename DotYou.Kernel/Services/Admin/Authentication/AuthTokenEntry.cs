using System;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class AuthTokenEntry
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Point in time the token expires
        /// </summary>
        public Int64 ExpiryUnixTime { get; set; }

        /// <summary>
        /// The Server's 1/2 of the KeK
        /// </summary>
        public Guid ServerKek { get; set; }
    }
}