using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Youverse.Core.Identity
{
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
