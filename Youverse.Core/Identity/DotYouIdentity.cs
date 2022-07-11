using System;
using System.Text.Json.Serialization;
using LiteDB;
using Serilog.Sinks.File;
using Youverse.Core.Util;

namespace Youverse.Core.Identity
{


    /// <summary>
    /// Holds the identity for an individual using the dotYou platform
    /// </summary>
    [JsonConverter(typeof(DotYouIdentityConverter))]
    public readonly struct DotYouIdentity
    {
        private readonly string _identifier;
        private readonly Guid _id;

        public DotYouIdentity(string identifier)
        {
            _identifier = identifier?.ToLower().Trim();
            if (string.IsNullOrEmpty(_identifier) == false)
            {
                //TODO: change to use yousha
                // new Guid(YouSHA.ReduceSHA256Hash(dotYouId.ToUtf8ByteArray()))
                _id = MiscUtils.MD5HashToGuid(_identifier);
            }
            else
            {
                _id = Guid.Empty;
            }
        }

        [JsonIgnore] public string Id => _identifier;

        public static bool operator ==(DotYouIdentity d1, DotYouIdentity d2)
        {
            return d1.ToGuid() == d2.ToGuid();
        }

        public static bool operator !=(DotYouIdentity d1, DotYouIdentity d2) => !(d1 == d2);

        public static implicit operator string(DotYouIdentity dy)
        {
            return dy._identifier;
        }

        public static explicit operator DotYouIdentity(string id)
        {
            return new DotYouIdentity(id);
        }

        public static implicit operator Guid(DotYouIdentity dotYouId)
        {
            return dotYouId.ToGuid();
        }

        //TODO: I don't like having an ref to LiteDB in this assembly.  need to find a better conversion
        public static implicit operator ObjectId(DotYouIdentity dotYouId)
        {
            return new ObjectId(dotYouId.ToGuid().ToByteArray());
        }

        public override bool Equals(object obj)
        {
            var d2 = (DotYouIdentity)obj;
            return this == d2;
        }

        public override int GetHashCode()
        {
            return _identifier?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return _identifier;
        }

        /// <summary>
        /// Returns an UniqueId of this DotYouIdentity using an MD5 hash converted to a Guid.  The value is converted to lower case before calculating the hash.
        /// </summary>
        /// <returns></returns>
        public Guid ToGuid()
        {
            // new Guid(YouSHA.ReduceSHA256Hash(dotYouId.ToUtf8ByteArray()))

            return this._id;
        }
    }
}