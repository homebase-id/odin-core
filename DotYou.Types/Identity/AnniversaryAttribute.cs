using Newtonsoft.Json;

namespace Identity.DataType.Attributes
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
