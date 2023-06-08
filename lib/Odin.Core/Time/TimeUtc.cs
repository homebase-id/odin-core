using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading;

namespace Youverse.Core
{
    public class TimeUtcConverter : JsonConverter<TimeUtc>
    {
        public override TimeUtc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {

            var values = reader.GetString()?.Split(":");

            if ((values == null) || (values.Length < 2))
            {
                //TODO: Todd wrote: need to return an empty timezone value here
                //      Michael thinks: There is no such thing as an empty timezone, time or date. 
                //                      They are structs and should have a value. Suppose you could choose to call "0" empty.
                return new TimeUtc(0, 0, 0);
            }

            var hours = Int32.Parse(values[0]);
            var minutes = Int32.Parse(values[1]);
            var seconds = Int32.Parse(values[2]);

            return new TimeUtc(hours, minutes, seconds);
        }
        public override void Write(Utf8JsonWriter writer, TimeUtc value, JsonSerializerOptions options)
        {
            var s = value.Hours.ToString("00") + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00");
            writer.WriteStringValue(s);
        }
    }

    [JsonConverter(typeof(TimeUtcConverter))]
    public readonly struct TimeUtc
    {
        /// <summary>
        /// Holds time in a three byte sortable structure in hours, minutes and seconds.
        /// </summary>
        /// <param name="hours"></param>
        /// <param name="minutes"></param>
        /// <param name="seconds"></param>
        public TimeUtc(int hours, int minutes, int seconds)
        {
            if ((hours < 0) || (hours > 23))
                throw new Exception("hours must be [0..23]");

            if ((minutes < 0) || (minutes > 59))
                throw new Exception("minutes must be [0..59]");

            if ((seconds < 0) || (seconds > 59))
                throw new Exception("seconds must be [0..59]");

            _time = new byte[3];
            _time[0] = (byte)hours;
            _time[1] = (byte)minutes;
            _time[2] = (byte)seconds;
        }

        public override string ToString()
        {
            return Hours.ToString("00") + ":" + Minutes.ToString("00") + ":" + Seconds.ToString("00");
        }

        public int Hours { get { return _time[0]; } }
        public int Minutes { get { return _time[1]; } }
        public int Seconds { get { return _time[2]; } }

        private readonly byte[] _time;
    }
}