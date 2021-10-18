﻿using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public class PictureAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Picture; } set { } }

        public override string ToString()
        {
            return this.Picture;
        }

        [JsonProperty("picture")]
        public string Picture { get; set; }
    }
}
