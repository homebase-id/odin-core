using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Identity;

namespace Odin.Services.Authorization.Acl
{
    [DebuggerDisplay("RSG:{RequiredSecurityGroup}, Circles:{RequiredCircleCount} OdinIds:{RequiredOdinIdCount}")]
    public class AccessControlList
    {
        public static AccessControlList OwnerOnly => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Owner };
        public static AccessControlList Authenticated => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Authenticated };
        public static AccessControlList Connected => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Connected };
        public static AccessControlList Anonymous => new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Anonymous };

        private SecurityGroupType _requiredSecurityGroup = SecurityGroupType.Owner;
        private bool _groupGiven;
        private int? _undefinedGroupGiven;

        /// <summary>
        /// The lowest caller level that may see the file.  Missing (the field starts at Owner), or undefined (0, which a
        /// serializer writes for a group nobody set, or any other number that names no rung), means <see cref="SecurityGroupType.Owner"/>:
        /// an ACL that names no group lets no one outside the identity in.  Set here, so every reader sees it, a stored header
        /// included (docs/file-access.md, "Writing").  A client's upload or update is refused an undefined group instead
        /// (<see cref="AssertGivenGroupIsDefined"/>).
        /// </summary>
        public SecurityGroupType RequiredSecurityGroup
        {
            get => _requiredSecurityGroup;
            set
            {
                _groupGiven = true;
                var isDefined = Enum.IsDefined(value);
                _undefinedGroupGiven = isDefined ? null : (int)value;
                _requiredSecurityGroup = isDefined ? value : SecurityGroupType.Owner;
            }
        }

        /// <summary>
        /// For an ACL a client sends with an upload or update: refuses a group that names no rung, so a client bug (778
        /// for Connected) is a 400, not a file nobody else can see.  Not for stored headers, which read it as Owner.
        /// </summary>
        public void AssertGivenGroupIsDefined()
        {
            if (_undefinedGroupGiven is { } value)
            {
                throw new OdinClientException($"{value} is not a required security group", OdinClientErrorCode.ArgumentError);
            }
        }

        /// <summary>
        /// A list of circles which can access this file.  The caller must below to at least one circle. To retrieve the list, use <see cref="GetRequiredCircles"/>.
        /// </summary>
        public List<Guid> CircleIdList { get; set; }

        /// <summary>
        /// Identities which can access this file.  Together with <see cref="CircleIdList"/>: the caller must be in one of the
        /// circles or be one of these identities, and must still meet <see cref="RequiredSecurityGroup"/>.
        /// </summary>
        public List<string> OdinIdList { get; set; }

        /// <summary>
        /// Returns the list of Circles that can access the file when the RequiredSecurityGroup is SecurityGroupType.CircleConnected
        /// </summary>
        public IEnumerable<Guid> GetRequiredCircles()
        {
            if (RequiredSecurityGroup is SecurityGroupType.Anonymous or SecurityGroupType.Owner)
            {
                //anonymous files don't allow circles to be set.
                //owner can view all files
                return Array.Empty<Guid>();
            }

            return (IEnumerable<Guid>)this.CircleIdList ?? Array.Empty<Guid>();
        }

        /// <summary>
        /// Returns the list of Identities that can access the file when the RequiredSecurityGroup is SecurityGroupType.CustomList
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetRequiredIdentities()
        {
            if (RequiredSecurityGroup is SecurityGroupType.Anonymous or SecurityGroupType.Owner)
            {
                //anonymous files don't allow an identity list
                //owner can view all files
                return Array.Empty<string>();
            }

            return (IEnumerable<string>)this.OdinIdList ?? Array.Empty<string>();
        }

        public void Validate()
        {
            if (RequiredSecurityGroup == SecurityGroupType.Anonymous || RequiredSecurityGroup == SecurityGroupType.Owner)
            {
                if ((this.CircleIdList?.Count() ?? 0) > 0 || (this.OdinIdList?.Count() ?? 0) > 0)
                {
                    throw new OdinClientException(
                        !_groupGiven || _undefinedGroupGiven != null
                            ? "No required security group was given, so it means owner, which can't name circles or identities"
                            : "Cannot specify circle or identity list when required security group is anonymous or owner",
                        OdinClientErrorCode.CannotAllowCirclesOrIdentitiesOnAnonymousOrOwnerOnly);
                }
            }

            // Named identities are indexed by their hash id, which must match the caller's: store them in
            // OdinId's canonical form, once each, and refuse what isn't one rather than fail at indexing.
            if (OdinIdList != null)
            {
                if (OdinIdList.Any(id => !OdinId.IsValid(id)))
                {
                    throw new OdinClientException("The identity list contains an invalid identity", OdinClientErrorCode.ArgumentError);
                }

                OdinIdList = OdinIdList.Select(id => ((OdinId)id).DomainName).Distinct().ToList();
            }

            //Note: this is now allowed because of YouAuth Domains`
            // if (RequiredSecurityGroup == SecurityGroupType.Authenticated)
            // {
            //     if ((this.CircleIdList?.Count() ?? 0) > 0)
            //     {
            //         throw new OdinClientException("Cannot specify circle list when required security group is authenticated", OdinClientErrorCode.CannotAllowCirclesOnAuthenticatedOnly);
            //     }
            // }
        }

        //

        private int RequiredCircleCount => CircleIdList?.Count ?? 0;

        private int RequiredOdinIdCount => OdinIdList?.Count ?? 0;
    }
}