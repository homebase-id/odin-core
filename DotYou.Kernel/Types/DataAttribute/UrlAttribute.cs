using Newtonsoft.Json;

namespace DotYou.Types.DataAttribute
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
