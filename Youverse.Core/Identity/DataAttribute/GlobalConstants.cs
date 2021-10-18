using System;

namespace Youverse.Core.Identity.DataAttribute
{
    public class GlobalConstants
    {
        public static long UnixTime()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static long UnixTimeOffset(int days = 0, int hours = 0, int minutes = 0, int seconds = 0)
        {
            return UnixTime() + seconds + 60 * minutes + 60 * 60 * hours + 24 * 60 * 60 * days;
        }

        // These are three built-in circles
        public const string CircleAnonymous = "@_anonymous";  // Any anonymous bloke browsing your site
        public const string CircleIdentified = "@_identified"; // an authenticated identity not in your network
        public const string CircleVerified = "@_verified";   // an authenticated identity not in your network which is SSL certificate verified as a real person.
    }
}