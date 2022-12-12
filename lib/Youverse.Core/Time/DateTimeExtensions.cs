using System;

namespace Youverse.Core
{
    public static class DateTimeExtensions
    {
        /* public static UInt64 UnixTimeMilliseconds()
        {
            return (UInt64) DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        public static UInt64 UnixTimeSeconds()
        {
            return (UInt64) DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }*/

        /*
        /// <summary>
        /// Returns a nicely formatted datetime (i.e. about 1 hour ago)
        /// Nicked from code project
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static string ToRelative(this DateTimeOffset dt)
        {
            TimeSpan span = DateTimeOffset.UtcNow - dt;
            if (span.Days > 365)
            {
                int years = (span.Days / 365);
                if (span.Days % 365 != 0)
                    years += 1;
                return String.Format($"about {years} year(s) ago");
            }
            if (span.Days > 30)
            {
                int months = (span.Days / 30);
                if (span.Days % 31 != 0)
                    months += 1;
                return String.Format("about {0} {1} ago",
                months, months == 1 ? "month" : "months");
            }
            if (span.Days > 0)
                return String.Format("about {0} {1} ago",
                span.Days, span.Days == 1 ? "day" : "days");
            if (span.Hours > 0)
                return String.Format("about {0} {1} ago",
                span.Hours, span.Hours == 1 ? "hour" : "hours");
            if (span.Minutes > 0)
                return String.Format("about {0} {1} ago",
                span.Minutes, span.Minutes == 1 ? "minute" : "minutes");
            if (span.Seconds > 5)
                return String.Format("about {0} seconds ago", span.Seconds);
            if (span.Seconds <= 5)
                return "just now";
            return string.Empty;
        }*/
    }
}
