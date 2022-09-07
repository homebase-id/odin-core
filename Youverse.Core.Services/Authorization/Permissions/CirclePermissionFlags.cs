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
        public static readonly int ReadConnections = 10;

        public static readonly int ReadConnectionRequests = 30;

        public static readonly int ReadCircleMembership = 50;
        
    }

    /// <summary>
    /// Specifies the permissions allowed for each permission group type
    /// </summary>
    public static class PermissionKeyAllowance
    {
        static PermissionKeyAllowance()
        {
            Apps = new ReadOnlyCollection<int>(new List<int>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.ReadConnectionRequests
            });

            Circles = new ReadOnlyCollection<int>(new List<int>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership
            });
        }

        public static bool IsValidAppPermission(int key)
        {
            return Apps.Any(k => k == key);
        }

        public static bool IsValidCirclePermission(int key)
        {
            return Circles.Any(k => k == key);
        }

        public static ReadOnlyCollection<int> Apps { get; }

        public static ReadOnlyCollection<int> Circles { get; }
    }
}