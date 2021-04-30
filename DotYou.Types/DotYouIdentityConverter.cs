using System;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace DotYou.Types
{
    //public class DotYouIdentityConverter : JsonConverter<DotYouIdentity>
    //{
    //    public override DotYouIdentity ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, DotYouIdentity existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    //    {
    //        var value = (string)reader.Value;
    //        return new DotYouIdentity(value);
    //    }

    //    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, DotYouIdentity value, Newtonsoft.Json.JsonSerializer serializer)
    //    {
    //        writer.WriteValue(value.ToString());
    //    }
    //}

    public class DotYouIdentityConverter : JsonConverter<DotYouIdentity>
    {
        public override DotYouIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            return new DotYouIdentity(value);
        }


        public override void Write(Utf8JsonWriter writer, DotYouIdentity value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
