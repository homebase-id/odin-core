﻿using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class CreditCardAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.CreditCard; } set { } }

        public override string ToString()
        {
            return this.Number + " " + this.Expiration + " " + this.Cvc;
        }

        [JsonProperty("number")]
        public string Number { get; set; }

        [JsonProperty("expiration")]
        public string Expiration { get; set; }

        [JsonProperty("cvc")]
        public string Cvc { get; set; }
    }
}
