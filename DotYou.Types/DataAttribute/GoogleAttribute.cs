using Newtonsoft.Json;

namespace DotYou.Types.DataAttribute
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
