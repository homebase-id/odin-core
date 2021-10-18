namespace Youverse.Core.Identity
{
    public class AvatarUri
    {
        public string Uri { get; set; }

        public override string ToString()
        {
            return this.Uri;
        }
    }
}