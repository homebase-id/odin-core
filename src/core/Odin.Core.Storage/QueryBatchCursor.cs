using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Storage
{
    public class TimeRowCursor
    {
        private UnixTimeUtc _timeUtc;

        [JsonPropertyName("time")]
        public UnixTimeUtc Time
        {
            get => _timeUtc;
            set
            {
                _timeUtc = value;

                if (_timeUtc.milliseconds > 1L << 42)
                {
                    _timeUtc = new UnixTimeUtc(Time.milliseconds >> 16);
                    logger.LogInfo("CursorLog: INFO TimeRowCursor shifted value >> 16 {shiftedValue} ", time.milliseconds);
                }
            }
        }

        [JsonPropertyName("row")]
        public long? rowId { get; set; }

        public TimeRowCursor(UnixTimeUtc time, long? rowId)
        {
            this.Time = time;
            this.rowId = rowId;
        }

        public override string ToString()
        {
            return Time.ToString() + "," + rowId.ToString();
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static TimeRowCursor FromJson(string json)
        {
            try
            {
                var deserializedCursor = JsonSerializer.Deserialize<TimeRowCursor>(json);
                return deserializedCursor;
            }
            catch (Exception)
            {
                return null;
            }
        }


        public static TimeRowCursor FromJsonOrOldString(string s)
        {
            if (s == null)
                return null;

            var cursor = TimeRowCursor.FromJson(s);

            if (cursor != null)
                return cursor;

            var parts = s.Split(',');

            if (parts.Length == 1 && long.TryParse(parts[0], out long ts))
            {
                logger.LogInfo("CursorLog: INFO TimeRowCursor original string one long {cursor} ", s);

                // This section is only for "old" cursors
                // Old cursors are in UnixTimeUtcUnique, so make them into a UnixTimeUtc
                if (ts > 1L << 42)
                {
                    ts = ts >> 16;
                    logger.LogInfo("CursorLog: INFO TimeRowCursor shifted value >> 16 {shiftedValue} ", ts);
                }

                return new TimeRowCursor(ts, null);
            }
            else if (parts.Length == 2 &&
                        long.TryParse(parts[0], out long ts2) &&
                        long.TryParse(parts[1], out long rowId))
            {
                logger.LogInfo("CursorLog: INFO TimeRowCursor string long pair {cursor} ", s);

                if (ts2 > 1L << 42)
                {
                    ts2 = ts2 >> 16;
                    logger.LogInfo("CursorLog: INFO TimeRowCursor shifted value >> 16 {shiftedValue} ", ts);
                }

                return new TimeRowCursor(ts2, rowId);
            }

            return null;
        }


        public bool Equals(TimeRowCursor other)
        {
            return this.Time.milliseconds == other.Time.milliseconds && this.rowId == other.rowId;
        }
    }

    public enum QueryBatchCursorType
    {
        Created = 1,
        UserDate = 2
    };

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
        public QueryBatchCursorType cursorType { get; set; }

        [JsonPropertyName("paging")] 
        public TimeRowCursor pagingCursor { get; set; }

        [JsonPropertyName("stop")]
        public TimeRowCursor stopAtBoundary { get; set; }

        [JsonPropertyName("next")]
        public TimeRowCursor nextBoundaryCursor { get; set; }

        public bool IsUserDateSort()
        {
            return cursorType == QueryBatchCursorType.UserDate;
        }

        public QueryBatchCursor()
        {
            pagingCursor = null;
            stopAtBoundary = null;
            nextBoundaryCursor = null;
        }


        public void CursorStartPoint(UnixTimeUtc startFromPoint)
        {
            pagingCursor = new TimeRowCursor(startFromPoint, null);

            nextBoundaryCursor = null;
            stopAtBoundary = null;
        }

        // This behaves DIFFERENTLY than the file ID did.
        // Before it would NOT include the row matching, however, no way around it
        // Now it WILL include the matching row.
        public void CursorStartPoint(UnixTimeUtc startFromPoint, bool IsUserDate)
        {
            CursorStartPoint(startFromPoint);
        }


        /// <summary>
        /// Creates a cursor that doesn't go back farther than the supplied item (fileId).
        /// Any item equalling stopAtBoundaryItem fileId won't be included in any result.
        /// This behaves DIFFERENTLY than the file ID did.
        /// Before it would NOT include the row matching, however, no way around it
        /// Now it WILL include the matching row.
        /// </summary>
        /// <param name="stopAtBoundaryItem">Go no further than this fileId</param>
        public QueryBatchCursor(UnixTimeUtc stopAtBoundaryItem)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            stopAtBoundary = new TimeRowCursor(stopAtBoundaryItem, null);
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

            stopAtBoundary = new TimeRowCursor(stopAtBoundaryUtc, null);
        }


        public QueryBatchCursor(string jsonString)
        {
            try
            {
                QueryBatchCursor deserializedCursor = JsonSerializer.Deserialize<QueryBatchCursor>(jsonString);
                this.pagingCursor = deserializedCursor.pagingCursor;
                this.nextBoundaryCursor = deserializedCursor.nextBoundaryCursor;
                this.stopAtBoundary = deserializedCursor.stopAtBoundary;
                this.cursorType = deserializedCursor.cursorType;
            }
            catch (Exception)
            {
                logger.LogInfo("CursorLog: ERR QueryBatchCursor unable to parse string {cursor} ", jsonString);

                pagingCursor = null;
                stopAtBoundary = null;
                nextBoundaryCursor = null;

                // Probably the old format
                /*
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
                 */
            }
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}