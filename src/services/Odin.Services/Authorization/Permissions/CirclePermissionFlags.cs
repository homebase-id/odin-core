using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Odin.Services.Authorization.Permissions
{
    public static class PermissionKeys
    {
        public const int ReadConnections = 10;

        public const int ReadConnectionRequests = 30;

        public const int ReadCircleMembership = 50;

        public const int ReadWhoIFollow = 80;

        public const int ReadMyFollowers = 130;
       
        public const int ManageFeed = 150;

        public const int ManageContacts = 160;

        public const int ManageProfile = 170;

        public const int UseTransitWrite = 210;

        public const int UseTransitRead = 305;

        public const int SendPushNotifications = 405;

        public const int PublishStaticContent = 505;
        
        public const int SendOnBehalfOfOwner = 707;
        
        /// <summary>
        /// Circles with this permission can introduce me to others
        /// </summary>
        public const int AllowIntroductions = 808;
        
        public const int SendIntroductions = 909;
        
        public static readonly List<int> All =
        [
            ReadConnections,
            ReadConnectionRequests,
            ReadCircleMembership,
            ReadWhoIFollow,
            ReadMyFollowers,
            UseTransitWrite,
            UseTransitRead,
            SendPushNotifications,
            ManageFeed,
            ManageContacts,
            ManageProfile,
            PublishStaticContent,
            AllowIntroductions,
            SendIntroductions
        ];
    }

    /// <summary>
    /// Permission keys implied by holding another key. Expanded when a permission context is
    /// created (<see cref="ExchangeGrants.ExchangeGrantService.CreatePermissionContext"/>), so a
    /// grant never needs to list them explicitly.
    /// </summary>
    public static class PermissionKeyImplications
    {
        private static readonly IReadOnlyDictionary<int, int[]> Implications = new Dictionary<int, int[]>
        {
            // Managing contacts requires reading the relationship state contacts mirror
            // (connection status, pending requests, circle membership).
            [PermissionKeys.ManageContacts] =
            [
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ReadCircleMembership
            ]
        };

        /// <summary>
        /// Returns the keys implied by <paramref name="grantedKeys"/> that are not already granted.
        /// </summary>
        public static List<int> ResolveImpliedKeys(IEnumerable<int> grantedKeys)
        {
            var granted = new HashSet<int>(grantedKeys);
            return Implications
                .Where(pair => granted.Contains(pair.Key))
                .SelectMany(pair => pair.Value)
                .Where(key => !granted.Contains(key))
                .Distinct()
                .ToList();
        }
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
                PermissionKeys.ManageContacts,
                PermissionKeys.ManageProfile,
                PermissionKeys.PublishStaticContent
            });

            Circles = new ReadOnlyCollection<int>(new List<int>()
            {
                PermissionKeys.ReadConnections,
                PermissionKeys.ReadCircleMembership,
                PermissionKeys.ReadWhoIFollow,
                PermissionKeys.UseTransitRead,

                //Note: circles can potentially useTransitWrite so feed items can be
                //distributed when posting to a group channel;  intentionally leaving out UseTransitRead
                PermissionKeys.SendOnBehalfOfOwner,
                // PermissionKeys.SendIntroductions,
                PermissionKeys.AllowIntroductions
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