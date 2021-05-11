using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class NickNameAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.NickName; } set { } }

        public override string ToString()
        {
            return this.NickName;
        }

        [JsonProperty("nickName")]
        public string NickName { get; set; }
    }
}
