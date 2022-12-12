using System;

/*
namespace Youverse.Core
{
    /// <summary>
    /// Specifies a start and end date in Utc offset using Unix Epoc in Seconds
    /// </summary>
    public class DateRangeOffset
    {
        public DateRangeOffset()
        {
        }

        /// <summary>
        /// Initializes new instance with the specified start and end dates
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public DateRangeOffset(UnixTimeUtc start, UnixTimeUtc end)
        {
            this.StartUnixTimeUtc = start;
            this.EndUnixTimeUtc = end;
        }

        /// <summary>
        /// The start DateTimeOffset in unix epoc seconds 
        /// </summary>
        public UnixTimeUtc StartUnixTimeUtc { get; set; }

        /// <summary>
        /// The end DateTimeOffset in unix epoc seconds
        /// </summary>
        public UnixTimeUtc EndUnixTimeUtc { get; set; }

        public bool IsBetween(UnixTimeUtc timestamp, bool inclusive)
        {
            if (inclusive)
            { 
                return timestamp >= this.StartUnixTimeUtc && timestamp <= this.EndUnixTimeUtc;
            }
            
            return timestamp > this.StartUnixTimeUtc && timestamp < this.EndUnixTimeUtc;
        }
    }
}*/