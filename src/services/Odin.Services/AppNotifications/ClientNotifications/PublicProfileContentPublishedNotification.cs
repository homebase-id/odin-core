using System;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Mediator;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public enum PublicProfileArtifact
    {
        /// <summary>sitedata.json, served at /cdn/sitedata.json</summary>
        SiteData = 1,

        /// <summary>public_image.json, served at /pub/image</summary>
        ProfileImage = 2,

        /// <summary>public_profile.json, served at /pub/profile</summary>
        ProfileCard = 3
    }

    /// <summary>
    /// Pushed to the owner's own sessions when <c>Odin.Services.Profile.ProfilePublishService</c>
    /// republishes one of the public, static-file artifacts derived from profile attributes. Distinct from
    /// the <see cref="IDriveNotification"/>s raised by the underlying attribute write itself (which cover
    /// the ProfileDrive file, not these derived artifacts) -- lets a client that renders/caches the public
    /// profile (e.g. a live preview of the public profile page) know to re-fetch the specific artifact that
    /// changed rather than poll or blindly re-fetch all three.
    /// </summary>
    public class PublicProfileContentPublishedNotification : MediatorNotificationBase, IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.PublicProfileContentPublished;

        public Guid NotificationTypeId { get; } = Guid.Parse("2f6b6e0a-6b2e-4b0a-8a3e-3e6b6b6e0a2f");

        /// <summary>Which artifact was just republished.</summary>
        public PublicProfileArtifact Artifact { get; init; }

        public string GetClientData()
        {
            return OdinSystemSerializer.Serialize(new
            {
                Artifact = this.Artifact.ToString()
            });
        }
    }
}
