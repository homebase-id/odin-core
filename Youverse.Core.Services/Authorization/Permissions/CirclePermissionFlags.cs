using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Youverse.Core.Services.Authorization.Permissions
{
    //TODO: need to breakout permissions that are only valid when the master key is present versus those which can be done by an app or youauth
    // [Flags]
    // public enum CirclePermissionFlags
    // {
    //     None = 0,
    //
    //     ReadConnections = 2,
    //
    //     ReadCircleMembership = 128,
    // }
    //
    // [Flags]
    // public enum AppPermissionFlags
    // {
    //     None = 0,
    //
    //     ReadConnections = 2,
    //
    //     ReadConnectionRequests = 32,
    //
    //     ReadCircleMembership = 128,
    // }

    public static class PermissionKeys
    {
        //TODO: change to integer
        public static readonly string ReadConnections = "ReadConnections";

        public static readonly string ReadConnectionRequests = "ReadConnectionRequests";

        public static readonly string ReadCircleMembership = "ReadCircleMembership";
    }

    /// <summary>
    /// Specifies the permissions allowed for each permission group type
    /// </summary>
    public static class PermissionKeyAllowance
    {
        static PermissionKeyAllowance()
        {
            Apps = new ReadOnlyCollection<string>(new List<string>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.ReadConnectionRequests
            });

            Circles = new ReadOnlyCollection<string>(new List<string>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership
            });
        }

        public static bool IsValidAppPermission(string key)
        {
            return Apps.Any(k => k.Equals(key, StringComparison.InvariantCultureIgnoreCase));
        }
        
        public static bool IsValidCirclePermission(string key)
        {
            return Circles.Any(k => k.Equals(key, StringComparison.InvariantCultureIgnoreCase));
        }

        public static ReadOnlyCollection<string> Apps { get; }

        public static ReadOnlyCollection<string> Circles { get; }
    }
}