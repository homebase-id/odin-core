using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
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
