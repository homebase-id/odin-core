using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit
{
    [JsonConverter(typeof(RecipientListJsonConverter))]
    public class RecipientList
    {
        public List<DotYouIdentity> Recipients { get; set; }
    }

    //TODO: uhg - Not sure why I had to build this for such a simple list.  maybe revisit one day
    public class RecipientListJsonConverter : JsonConverter<RecipientList>
    {
        public override void WriteJson(JsonWriter writer, RecipientList? value, JsonSerializer serializer)
        {
            if (value != null)
            {
                writer.WriteStartArray();

                foreach (var dotYouId in value.Recipients)
                {
                    writer.WriteValue(dotYouId.Id);
                }

                writer.WriteEndArray();
            }
        }

        public override RecipientList? ReadJson(JsonReader reader, Type objectType, RecipientList? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var list = hasExistingValue ? existingValue : new RecipientList() {Recipients = new List<DotYouIdentity>()};

            while (reader.Read())
            {
                var value = reader.Value as string;
                if (null != value)
                {
                    list.Recipients.Insert(0, (DotYouIdentity) value);
                }
            }

            return list;
        }
    }
}