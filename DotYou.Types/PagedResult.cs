using System;
using System.Collections.Generic;

namespace DotYou.Types
{

    public class PagedResult<T>
    {
        public PagedResult()
        {

        }

        public PagedResult(PageRequest req, int totalPages, IList<T> results)
        {
            if (null == req)
            {
                throw new ArgumentNullException(nameof(req));
            }

            if (null == results)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (results.Count > 0 && totalPages < 1)
            {
                throw new Exception("The number of results is greater than zero but your total pages is less than 1.  How is this possible?");
            }

            Request = req;
            TotalPages = totalPages;
            Results = results;
        }

        public PageRequest Request { get; set; }

        public int TotalPages { get; set; }

        public IList<T> Results { get; set; }
    }
}
