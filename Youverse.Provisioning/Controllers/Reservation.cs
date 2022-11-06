using Youverse.Core;
using Youverse.Core.Util;

namespace Youverse.Provisioning.Controllers
{
    public class Reservation
    {
        private Guid _domainKey;
        private string _domain;

        public Guid Id { get; set; }
        
        public string Domain
        {
            get => _domain;
            set
            {
                _domain = value;
                _domainKey = HashUtil.ReduceSHA256Hash(value);
            }
        }

        public Guid DomainKey
        {
            get { return _domainKey; }
        }

        public UnixTimeUtc CreatedTime { get; set; }

        public UnixTimeUtc ExpiresTime { get; set; }
    }
}