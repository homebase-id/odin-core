using System;
using Odin.Core.Time;

namespace Odin.Core.Storage
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
        public UnixTimeUtc? userDatePagingCursor;
        public byte[] stopAtBoundary;
        public UnixTimeUtc? userDateStopAtBoundary;
        public byte[] nextBoundaryCursor;
        public UnixTimeUtc? userDateNextBoundaryCursor;

        public bool IsUserDateSort()
        {
            return userDatePagingCursor != null || userDateNextBoundaryCursor != null || userDateStopAtBoundary != null;
        }

        public QueryBatchCursor()
        {
            pagingCursor = null;
            stopAtBoundary = null;
            nextBoundaryCursor = null;
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;
        }


        public void CursorStartPoint(byte[] startFromPoint)
        {
            pagingCursor = new byte[16];
            startFromPoint.CopyTo(pagingCursor, 0);

            nextBoundaryCursor = null;
            stopAtBoundary = null;
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;
        }

        public void CursorStartPoint(UnixTimeUtc startFromPoint, bool IsUserDate)
        {
            var _g = SequentialGuid.CreateGuid(startFromPoint);
            CursorStartPoint(_g.ToByteArray());
            if (IsUserDate)
                userDatePagingCursor = startFromPoint;
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


        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied timestamp.
        /// No tests written, function not needed (yet).
        /// </summary>
        /// <param name="startFromPoint">The time at which to go back no further</param>
        /// <returns>A cursor that will go back (or forward) no further in time than the supplied parameter</returns>
        public QueryBatchCursor(UnixTimeUtc stopAtBoundaryUtc, bool IsUserDate)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            userDatePagingCursor = null;
            userDateStopAtBoundary = null;
            userDateNextBoundaryCursor = null;

            var _g = SequentialGuid.CreateGuid(stopAtBoundaryUtc);
            stopAtBoundary = new byte[16];
            _g.ToByteArray().CopyTo(stopAtBoundary, 0);

            if (IsUserDate)
                userDateStopAtBoundary = stopAtBoundaryUtc;
        }


        public QueryBatchCursor(string base64CursorState)
        {
            var bytes = Convert.FromBase64String(base64CursorState);
            if (bytes.Length == 3 * 16)
            {
                (pagingCursor, stopAtBoundary, nextBoundaryCursor) = ByteArrayUtil.Split(bytes, 16, 16, 16);

                if (ByteArrayUtil.EquiByteArrayCompare(pagingCursor, Guid.Empty.ToByteArray()))
                    pagingCursor = null;

                if (ByteArrayUtil.EquiByteArrayCompare(stopAtBoundary, Guid.Empty.ToByteArray()))
                    stopAtBoundary = null;

                if (ByteArrayUtil.EquiByteArrayCompare(nextBoundaryCursor, Guid.Empty.ToByteArray()))
                    nextBoundaryCursor = null;
            }
            else if (bytes.Length == 3 * 16 + 3 * 1 + 3 * 8)
            {
                var (c1, nullBytes, c2) = ByteArrayUtil.Split(bytes, 3*16, 3*1, 3*8);

                (pagingCursor, stopAtBoundary, nextBoundaryCursor) = ByteArrayUtil.Split(c1, 16, 16, 16);

                if (ByteArrayUtil.EquiByteArrayCompare(pagingCursor, Guid.Empty.ToByteArray()))
                    pagingCursor = null;

                if (ByteArrayUtil.EquiByteArrayCompare(stopAtBoundary, Guid.Empty.ToByteArray()))
                    stopAtBoundary = null;

                if (ByteArrayUtil.EquiByteArrayCompare(nextBoundaryCursor, Guid.Empty.ToByteArray()))
                    nextBoundaryCursor = null;

                var (bd1,bd2,bd3) = ByteArrayUtil.Split(c2, 8, 8, 8);

                userDatePagingCursor = ByteArrayUtil.BytesToInt64(bd1);
                if (nullBytes[0] == 0)
                    userDatePagingCursor = null;

                userDateStopAtBoundary = ByteArrayUtil.BytesToInt64(bd2);
                if (nullBytes[1] == 0)
                    userDateStopAtBoundary = null;

                userDateNextBoundaryCursor = ByteArrayUtil.BytesToInt64(bd3);
                if (nullBytes[2] == 0)
                    userDateNextBoundaryCursor = null;
            }
            else
                throw new Exception("Invalid cursor state");
        }


        public string ToState()
        {
            var bytes = ByteArrayUtil.Combine(
                this.pagingCursor ?? Guid.Empty.ToByteArray(),
                this.stopAtBoundary ?? Guid.Empty.ToByteArray(),
                this.nextBoundaryCursor ?? Guid.Empty.ToByteArray());
            
            if ((userDatePagingCursor == null) && (userDateStopAtBoundary == null) && (userDateNextBoundaryCursor == null))
                return bytes.ToBase64();

            var nullBytes = ByteArrayUtil.Combine(
                this.userDatePagingCursor == null ? new byte[] { 0 } : new byte[] { 1 },
                this.userDateStopAtBoundary == null ? new byte[] { 0 } : new byte[] { 1 },
                this.userDateNextBoundaryCursor == null ? new byte[] { 0 } : new byte[] { 1 });

            var bytes2 = ByteArrayUtil.Combine(ByteArrayUtil.Int64ToBytes(this.userDatePagingCursor?.milliseconds ?? long.MinValue),
                                      ByteArrayUtil.Int64ToBytes(this.userDateStopAtBoundary?.milliseconds ?? long.MinValue),
                                      ByteArrayUtil.Int64ToBytes(this.userDateNextBoundaryCursor?.milliseconds ?? long.MinValue));

            bytes = ByteArrayUtil.Combine(bytes, nullBytes, bytes2);

            return bytes.ToBase64();
        }
    }
}