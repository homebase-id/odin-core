namespace Odin.Services.Peer;

public static class PeerApiPathConstants
{
    public const string BasePathV1 = "/api/peer/v1";
    public const string HostV1 = BasePathV1 + "/host";
    
    public const string SecurityV1 = HostV1 + "/security";
    public const string DriveV1 = HostV1 + "/drives";
    public const string FeedV1 = HostV1 + "/feed";
    public const string ReactionsV1 = HostV1 + "/reactions";
    public const string InvitationsV1 = HostV1 + "/invitations";
    public const string FollowersV1 = HostV1 + "/followers";
    public const string EncryptionV1 = HostV1 + "/encryption";
}