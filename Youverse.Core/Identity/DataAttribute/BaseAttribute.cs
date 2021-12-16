using System;
using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public abstract class BaseAttribute
    {
        [JsonProperty("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonProperty("attributeType")]
        public abstract int AttributeType { get; set; }

        /// <summary>
        /// Enables grouping attributes (i.e. useful for a UI as section headers)  
        /// </summary>
        public virtual Guid CategoryId { get; set; }
        
        public string Label { get; set; }
    }
    
    
}
