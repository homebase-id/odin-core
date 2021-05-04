﻿using System;

namespace DotYou.Types
{

    /// <summary>
    /// Specifies details for how an <see cref="IEnumerable<T>"/> should be returned
    /// </summary>
    public class PageOptions
    {
        public readonly static PageOptions Default = new PageOptions(1, 55);

        public PageOptions() { }
        public PageOptions(int pageNumber, int pageSize)
        {
            if (pageSize < 1)
            {
                throw new Exception("Page size must be more than 1");
            }

            if (pageNumber < 1)
            {
                throw new Exception("Page Number must be greater than 0");
            }

            PageNumber = pageNumber;
            PageSize = pageSize;
        }

        /// <summary>
        /// The page number (starting from 1)
        /// </summary>
        public int PageNumber { get; private set; }

        /// <summary>
        /// Number of records per page
        /// </summary>
        public int PageSize { get; private set; }

        /// <summary>
        /// Returns the total number of pages for the given number of records.
        /// </summary>
        public int GetTotalPages(long recordCount)
        {
            return (int)Math.Ceiling((double)recordCount / PageSize);
        }

        /// <summary>
        /// Returns the number of records to skip based on <see cref="PageSize"/> and <see cref="PageNumber"/>
        /// </summary>
        /// <returns></returns>
        public int GetSkipCount()
        {
            return PageSize * PageIndex;
        }

        /// <summary>
        /// Returns the page index (0 based)
        /// </summary>
        public int PageIndex
        {
            get { return PageNumber - 1; }
        }

        /// <summary>
        /// Returns the querystring format
        /// </summary>
        /// <param name="format"></param>
        /// <returns></returns>
        public string ToQueryStringParams()
        {
            return $"pageNumber={PageNumber}&pageSize={PageSize}";
        }
    }

}
