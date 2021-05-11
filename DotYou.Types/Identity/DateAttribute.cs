using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class DateAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Date; } set { } }

        public override string ToString()
        {
            return this.Date;
        }

        [JsonProperty("date")]
        public string Date { get; set; }
    }
}
