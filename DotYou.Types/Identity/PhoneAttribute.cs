using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class PhoneAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Phone; } set { } }

        public override string ToString()
        {
            return this.CountryCode + " " + this.Number;
        }

        [JsonProperty("countryCode")]
        public string CountryCode { get; set; }

        [JsonProperty("number")]
        public string Number { get; set; }
    }
}
