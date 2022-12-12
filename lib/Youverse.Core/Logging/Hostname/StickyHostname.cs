using System.Threading;

namespace Youverse.Core.Logging.Hostname
{
    public class StickyHostname : IStickyHostname
    {
        private static readonly AsyncLocal<string> _hostName = new();
        private readonly IStickyHostnameGenerator _generator;

        public StickyHostname(IStickyHostnameGenerator generator)
        {
            _generator = generator;
        }
        
        public string Hostname
        {
            get => _hostName.Value ?? (_hostName.Value = _generator.Generate());
            set => _hostName.Value = value;
        }
    }
}