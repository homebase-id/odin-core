using System;
using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Services.Registry.Registration
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
                _domainKey = ByteArrayUtil.ReduceSHA256Hash(value);
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