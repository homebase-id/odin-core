using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class TwitterAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Twitter; } set { } }

        public override string ToString()
        {
            return this.Twitter;
        }

        [JsonProperty("twitter")]
        public string Twitter { get; set; }
    }
}
