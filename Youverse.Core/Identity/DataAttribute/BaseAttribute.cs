using System;
using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    // XXX I'd like the Id and AttrType to be readonly 
    public abstract class BaseAttribute
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("attributeType")]
        public abstract int AttributeType { get; set; }

        public virtual Guid CategoryId { get; set; }
    }
}
