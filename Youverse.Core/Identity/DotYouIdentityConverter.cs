
using System;
using Newtonsoft.Json;

namespace Youverse.Core.Identity
{
    public class DotYouIdentityConverter : JsonConverter<DotYouIdentity>
    {
        // public override DotYouIdentity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        // {
        //     var value = reader.GetString();
        //     return new DotYouIdentity(value);
        // }
        //
        //
        // public override void Write(Utf8JsonWriter writer, DotYouIdentity value, JsonSerializerOptions options)
        // {
        //     writer.WriteStringValue(value.ToString());
        // }

        public override DotYouIdentity ReadJson(JsonReader reader, Type objectType, DotYouIdentity existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var value = reader.ReadAsString();
            return new DotYouIdentity(value);
        }

        public override void WriteJson(JsonWriter writer, DotYouIdentity value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }
    }
}
