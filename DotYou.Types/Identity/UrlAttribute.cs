using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class UrlAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.URL; } set { } }

        public override string ToString()
        {
            return this.Url;
        }

        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
