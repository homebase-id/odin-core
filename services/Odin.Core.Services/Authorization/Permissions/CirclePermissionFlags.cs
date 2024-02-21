using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Odin.Core.Services.Authorization.Permissions
{
    public static class PermissionKeys
    {
        public const int ReadConnections = 10;

        public const int ReadConnectionRequests = 30;

        public const int ReadCircleMembership = 50;

        public const int ReadWhoIFollow = 80;

        public const int ReadMyFollowers = 130;
       
        public const int ManageFeed = 150;

        public const int UseTransitWrite = 210;

        public const int UseTransitRead = 305;

        public const int SendPushNotifications = 405;

        public const int PublishStaticContent = 505;
        
        public static readonly List<int> All = new List<int>()
        {
            ReadConnections,
            ReadConnectionRequests,
            ReadCircleMembership,
            ReadWhoIFollow,
            ReadMyFollowers,
            UseTransitWrite,
            UseTransitRead,
            SendPushNotifications,
            ManageFeed,
            PublishStaticContent
        };
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
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadWhoIFollow,
                PermissionKeys.UseTransitWrite,
                PermissionKeys.UseTransitRead,
                PermissionKeys.SendPushNotifications,
                PermissionKeys.ManageFeed,
                PermissionKeys.PublishStaticContent
            });

            Circles = new ReadOnlyCollection<int>(new List<int>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.ReadWhoIFollow
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