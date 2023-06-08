using System;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Services.Registry.Registration
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