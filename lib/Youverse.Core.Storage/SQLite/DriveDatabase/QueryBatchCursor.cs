using System;
using System.Linq;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class QueryBatchCursor
    {
        /// <summary>
        /// pagingCursor: points to the last record of data received in a query or null if there is no more data.
        /// If the pagingCursor is set, when retrieving data, depending on the 
        /// direction of the query, we get the data which is older than (less than) or
        /// newer than the current paging cursor.
        /// 
        /// stopAtBoundary: Paging will stop at this boundary (and that item won't be included in any result set)
        /// 
        /// nextBoundaryCursur: Used by the QueryBatchAuto() to manage getting continuous datasets.
        /// 
        /// </summary>
        public byte[] pagingCursor;
        public Int64? userDatePagingCursor;
        public byte[] stopAtBoundary;
        public Int64? userDateStopAtBoundary;
        public byte[] nextBoundaryCursor;
        public Int64? userDateNextBoundaryCursor;

        public QueryBatchCursor()
        {
            pagingCursor = null;
            stopAtBoundary = null;
            nextBoundaryCursor = null;
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;
        }

        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied timestamp.
        /// No tests written, function not needed (yet).
        /// </summary>
        /// <param name="stopAtBoundaryUtc">The time at which to go back no further</param>
        /// <returns>A cursor that will go back (or forward) no further in time than the supplied parameter</returns>
        public QueryBatchCursor(UnixTimeUtc stopAtBoundaryUtc)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            var _g = SequentialGuid.CreateGuid(stopAtBoundaryUtc);
            stopAtBoundary = new byte[16];
            _g.ToByteArray().CopyTo(stopAtBoundary, 0);
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;
        }

        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied item (fileId).
        /// Any item equalling stopAtBoundaryItem fileId won't be included in any result.
        /// </summary>
        /// <param name="stopAtBoundaryItem">Go no further than this fileId</param>
        public QueryBatchCursor(byte[] stopAtBoundaryItem)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            stopAtBoundary = new byte[16];
            stopAtBoundaryItem.CopyTo(stopAtBoundary, 0);
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;
        }


        public QueryBatchCursor(string base64CursorState)
        {
            var bytes = Convert.FromBase64String(base64CursorState);
            if (bytes.Length != 3 * 16)
                throw new Exception("Invalid cursor state");

            (pagingCursor, stopAtBoundary, nextBoundaryCursor) = ByteArrayUtil.Split(bytes, 16, 16, 16);

            if (ByteArrayUtil.EquiByteArrayCompare(pagingCursor, Guid.Empty.ToByteArray()))
                pagingCursor = null;

            if (ByteArrayUtil.EquiByteArrayCompare(stopAtBoundary, Guid.Empty.ToByteArray()))
                stopAtBoundary = null;

            if (ByteArrayUtil.EquiByteArrayCompare(nextBoundaryCursor, Guid.Empty.ToByteArray()))
                nextBoundaryCursor = null;
        }


        public string ToState()
        {
            var bytes = ByteArrayUtil.Combine(
                this.pagingCursor ?? Guid.Empty.ToByteArray(),
                this.stopAtBoundary ?? Guid.Empty.ToByteArray(),
                this.nextBoundaryCursor ?? Guid.Empty.ToByteArray());
            
            return bytes.ToBase64();
        }
    }
}