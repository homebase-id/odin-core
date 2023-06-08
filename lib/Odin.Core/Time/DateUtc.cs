using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Youverse.Core
{
    public class DateUtcConverter : JsonConverter<DateUtc>
    {
        public override DateUtc Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = reader.GetString()?.Split("-");

            if ((values == null) || (values.Length < 3))
            {
                return new DateUtc(0, 0, 0);
            }

            if (values[0] == "")
            {
                // Negative year
                var year = -Int32.Parse(values[1]);
                var month = Int32.Parse(values[2]);
                var day = Int32.Parse(values[3]);
                return new DateUtc(year, month, day);
            }
            else
            {
                var year = Int32.Parse(values[0]);
                var month = Int32.Parse(values[1]);
                var day = Int32.Parse(values[2]);
                return new DateUtc(year, month, day);
            }
        }

        public override void Write(Utf8JsonWriter writer, DateUtc value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"{value.Year}-{value.Month}-{value.Day}");
        }
    }


    /// <summary>
    /// Holds a user date in 5 byte sortable year, month, day structure
    /// Year is a signed 16 bit value, astronimical year (hence the CE after the ToString())
    /// </summary>
    /// 
    [JsonConverter(typeof(DateUtcConverter))]
    public readonly struct DateUtc
    {
        public DateUtc(int year, int month, int day)
        {
            if ((year < -9999) || (year > 9999))
                throw new Exception($"year must be between -9999..9999");

            if ((month < 1) || (month > 12))
                throw new Exception($"month must be between 1..12");

            if ((day < 1) || (day > 31))
                throw new Exception($"day must be between 1..31");

            // TODO: Add check for days in month for February ... leap year magic...

            _date = new byte[5];
            _date[0] = (byte) (year >> 8);
            _date[1] = (byte) year;
            _date[2] = (byte) month;
            _date[3] = (byte) day;
        }

        // TODO: Have a problem here? If year < 0 will it then sorts higher? Do we instead need to count years since a certain year?
        public int Year { get { Int16 y = (Int16) ((_date[0]<<8) | _date[1]);  return y; } }
        public int Month { get { return _date[2]; } }
        public int Day { get { return _date[3]; } }

        // https://en.wikipedia.org/wiki/Astronomical_year_numbering
        //
        public override string ToString()
        {
            return Year.ToString() + "-" + Month.ToString("00") + "-" + Day.ToString("00")+ " CE";
        }

        private readonly byte[] _date;
    }
}