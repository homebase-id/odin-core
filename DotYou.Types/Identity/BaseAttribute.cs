using System;
using Newtonsoft.Json;

namespace DotYou.Types.Identity
{
    // XXX I'd like the Id and AttrType to be readonly 
    public abstract class BaseAttribute
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("attributeType")]
        public abstract int AttributeType { get; set; }

    }
}
