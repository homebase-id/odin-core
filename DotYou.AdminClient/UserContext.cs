using System;
using DotYou.Types;

namespace DotYou.AdminClient
{
    public class UserContext
    {
        public string GivenName { get; set; }
        public string Surname { get; set; }

        /// <summary>
        /// The identity of the actor (i.e. frodobaggins.me, odin.vahalla.com)
        /// </summary>
        public string Identity { get; set; }

        public AvatarUri AvatarUri { get; set; }

    }
}