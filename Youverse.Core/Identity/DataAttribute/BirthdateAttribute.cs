﻿using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public class BirthdateAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Birthdate; } set { } }

        public override string ToString()
        {
            return this.Birthdate;
        }

        [JsonProperty("birthdate")]
        public string Birthdate { get; set; }
    }
}
