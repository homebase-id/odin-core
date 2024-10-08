﻿namespace Odin.Services.Authentication.Owner
{
    public static class OwnerAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating an individual to the Digital Identity they own
        /// </summary>
        public const string SchemeName = "digital-identity-owner";

        /// <summary>
        /// The name of the key used to store the token in cookies, dictionaries, etc.
        /// </summary>
        public const string CookieName = "DY0810";
        
    }
}