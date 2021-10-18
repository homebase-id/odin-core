using System;

namespace Youverse.Core
{
    /// <summary>
    /// A set of results from a query which is based on a given date range.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DateRangePagedResult<T> : PagedResult<T>
    {
        /// <summary>
        /// The start DateTimeOffset in unix epoc seconds 
        /// </summary>
        public Int64 StartDateTimeOffsetSeconds { get; set; }

        /// <summary>
        /// The end DateTimeOffset in unix epoc seconds
        /// </summary>
        public Int64 EndDateTimeOffsetSeconds { get; set; }
    }

    /// <summary>
    /// Specifies a start and end date in Utc offset using Unix Epoc in Seconds
    /// </summary>
    public class DateRangeOffset
    {
        public DateRangeOffset() { }

        /// <summary>
        /// Initializes new instance with the specified start and end dates
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        public DateRangeOffset(DateTimeOffset start, DateTimeOffset end)
        {
            this.StartDateTimeOffsetSeconds = start.ToUnixTimeSeconds();
            this.EndDateTimeOffsetSeconds = end.ToUnixTimeSeconds();
        }
        
        /// <summary>
        /// The start DateTimeOffset in unix epoc seconds 
        /// </summary>
        public Int64 StartDateTimeOffsetSeconds { get; set; }

        /// <summary>
        /// The end DateTimeOffset in unix epoc seconds
        /// </summary>
        public Int64 EndDateTimeOffsetSeconds { get; set; }
    }
}