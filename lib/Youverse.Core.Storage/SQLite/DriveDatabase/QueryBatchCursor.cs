using System;

namespace Youverse.Core.Storage.Sqlite.DriveDatabase
{
    public class QueryBatchCursor
    {
        /// <summary>
        /// pagingCursor keeps track of the pages. When fetching N records of data
        /// set the pagingCursor to the last record of data received, or null if there
        /// is no more data. If the pagingCursor is set, when retrieving data, get the
        /// data which is older than (less than) the current paging cursor.
        /// </summary>
        public byte[] pagingCursor;
        public byte[] currentBoundaryCursor;
        public byte[] nextBoundaryCursor;

        public QueryBatchCursor()
        {
            pagingCursor = null;
            currentBoundaryCursor = null;
            nextBoundaryCursor = null;
        }


        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied timestamp.
        /// No tests written, function not needed (yet).
        /// </summary>
        /// <param name="unixTimeSecondsStopAt">The UNIX time in seconds at which to go back no further</param>
        /// <returns>A cursor that will go back no further in time than the supplied parameter</returns>
        public QueryBatchCursor(UnixTimeUtc unixTimeSecondsStopAt)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            currentBoundaryCursor = SequentialGuid.CreateGuid(new UnixTimeUtc(unixTimeSecondsStopAt)).ToByteArray();
        }


        public QueryBatchCursor(string base64CursorState)
        {
            var bytes = Convert.FromBase64String(base64CursorState);
            if (bytes.Length != 3 * 16)
                throw new Exception("Invalid cursor state");

            (pagingCursor, currentBoundaryCursor, nextBoundaryCursor) = ByteArrayUtil.Split(bytes, 16, 16, 16);

            if (ByteArrayUtil.EquiByteArrayCompare(pagingCursor, Guid.Empty.ToByteArray()))
                pagingCursor = null;

            if (ByteArrayUtil.EquiByteArrayCompare(currentBoundaryCursor, Guid.Empty.ToByteArray()))
                currentBoundaryCursor = null;

            if (ByteArrayUtil.EquiByteArrayCompare(nextBoundaryCursor, Guid.Empty.ToByteArray()))
                nextBoundaryCursor = null;
        }


        public string ToState()
        {
            var bytes = ByteArrayUtil.Combine(
                this.pagingCursor ?? Guid.Empty.ToByteArray(),
                this.currentBoundaryCursor ?? Guid.Empty.ToByteArray(),
                this.nextBoundaryCursor ?? Guid.Empty.ToByteArray());
            
            return bytes.ToBase64();
        }
    }
}