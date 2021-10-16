using Newtonsoft.Json;

namespace DotYou.Types.DataAttribute
{
    public class ProfilePicAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.ProfilePic; } set { } }

        public override string ToString()
        {
            return this.ProfilePic;
        }

        [JsonProperty("profilePic")]
        public string ProfilePic { get; set; }
    }
}
