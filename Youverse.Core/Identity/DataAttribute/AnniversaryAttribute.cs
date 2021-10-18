﻿using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public class AnniversaryAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Anniversary; } set { } }

        public override string ToString()
        {
            return this.Anniversary;
        }

        [JsonProperty("anniversary")]
        public string Anniversary { get; set; }
    }
}
