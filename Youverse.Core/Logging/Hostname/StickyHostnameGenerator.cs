namespace Youverse.Core.Logging.Hostname
{
    public class StickyHostnameGenerator : IStickyHostnameGenerator
    {
        public string Generate()
        {
            return "localhost";
        }
    }
}