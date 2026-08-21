using Odin.Core;

namespace Odin.Services.Authorization.Apps
{
    /// <summary>
    /// The owner's consent for an app's grant-on-connect circles.
    /// </summary>
    public class SetConnectEnrollmentRequest
    {
        public GuidId AppId { get; set; }

        public bool Enabled { get; set; }
    }
}
