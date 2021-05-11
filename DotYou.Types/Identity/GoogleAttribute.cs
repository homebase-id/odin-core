using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class GoogleAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Google; } set { } }

        public override string ToString()
        {
            return this.Google;
        }

        [JsonProperty("google")]
        public string Google { get; set; }
    }
}
