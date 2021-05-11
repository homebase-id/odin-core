namespace DotYou.AdminClient
{
    public class UserContext
    {
        public string GivenName { get; set; }
        public string Surname { get; set; }

        public bool IsAuthenticated { get; set; }

        /// <summary>
        /// The identity of the actor (i.e. frodobaggins.me, odin.vahalla.com)
        /// </summary>
        public string Identity { get; set; }

        public string AvatarUri { get; set; }
    }
}