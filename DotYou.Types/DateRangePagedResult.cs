using System;

namespace DotYou.Types
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
}