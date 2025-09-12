using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Odin.Core.Time;

namespace Odin.Core.Storage
{
    public class TimeRowCursor
    {
        [JsonPropertyName("time")]
        public UnixTimeUtc Time { get; set; }

        [JsonPropertyName("row")] public long? rowId { get; set; }

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
            if (string.IsNullOrEmpty(json))
            {
                // I know the exception below handles it but,
                // I became very annoyed at hitting this during debugging
                return null;
            }
            
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

        public bool Equals(TimeRowCursor other)
        {
            return this.Time.milliseconds == other.Time.milliseconds && this.rowId == other.rowId;
        }
    }

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
        [JsonPropertyName("paging")]
        public TimeRowCursor pagingCursor { get; set; }

        [JsonPropertyName("stop")] public TimeRowCursor stopAtBoundary { get; set; }

        [JsonPropertyName("next")] public TimeRowCursor nextBoundaryCursor { get; set; }

        public QueryBatchCursor()
        {
            pagingCursor = null;
            stopAtBoundary = null;
            nextBoundaryCursor = null;
        }


        /// <summary>
        /// THIS FUNCTION SHOULD ONLY BE USED WITH OLDESTFIRST SORT ORDERING
        /// Sets the cursor so that it will retrieve pages from the startFromPoint
        /// If you do not specify a rowId, then the startPoint is inclusive
        /// If you do specify a rowId as well, the item you're passing in will not
        /// be inclusive
        /// </summary>
        /// <param name="startFromPoint">UnixTimeUtc of the cursor (userDate, created, modified)</param>
        public void CursorStartPoint(UnixTimeUtc startFromPoint, long ?rowId = null)
        {
            pagingCursor = new TimeRowCursor(startFromPoint, rowId);

            nextBoundaryCursor = null;
            stopAtBoundary = null;
        }

        public static QueryBatchCursor FromStartPoint(UnixTimeUtc fromTimestamp, long? rowId = null)
        {
            var c = new QueryBatchCursor();
            c.CursorStartPoint(fromTimestamp, rowId);
            return c;
        }

        /// <summary>
        /// Creates a cursor that stops at the supplied time boundary.
        /// Rows matching the boundary will be included in the result.
        /// </summary>
        /// <param name="stopAtBoundaryItem">Go no further than this timestamp</param>
        public QueryBatchCursor(UnixTimeUtc stopAtBoundaryItem)
        {
            pagingCursor = null;
            nextBoundaryCursor = null;
            stopAtBoundary = new TimeRowCursor(stopAtBoundaryItem, null);
        }


        public QueryBatchCursor(string jsonString)
        {
            try
            {
                QueryBatchCursor deserializedCursor = JsonSerializer.Deserialize<QueryBatchCursor>(jsonString);
                this.pagingCursor = deserializedCursor.pagingCursor;
                this.nextBoundaryCursor = deserializedCursor.nextBoundaryCursor;
                this.stopAtBoundary = deserializedCursor.stopAtBoundary;
            }
            catch (Exception)
            {
                pagingCursor = null;
                stopAtBoundary = null;
                nextBoundaryCursor = null;
            }
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}