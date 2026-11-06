namespace Odin.Hosting.UnifiedV2;

public static class UnifiedApiRouteConstants
{
    public const string BasePath = "/api/v2";
    public const string Auth = BasePath + "/auth";
    public const string Connections = BasePath + "/connections";
    public const string Followers = BasePath + "/followers";
    public const string Contacts = BasePath + "/contacts";
    public const string Profile = BasePath + "/profile";
    public const string Onboarding = BasePath + "/onboarding";
    public const string Health = BasePath + "/health";
    public const string Version = BasePath + "/version";
    public const string DrivesRoot = BasePath + "/drives";
    public const string ByDriveId = DrivesRoot + "/{driveId:guid}";
    public const string FilesRoot = ByDriveId + "/files";
    public const string ByFileId = FilesRoot + "/{fileId:guid}";
    public const string ReactionsByFileId = FilesRoot + "/{fileId:guid}/reactions";
    public const string GroupReactionsByFileId = FilesRoot + "/{fileId:guid}/group-reactions";
    public const string ByUniqueId = FilesRoot + "/by-uid/{uid:guid}";
    public const string ByGtid = FilesRoot + "/by-gtid/{gtid:guid}";
    public const string GroupReactionsByGtid = ByGtid + "/group-reactions";
    public const string Notify = BasePath + "/notify/push";
    public const string LiveRelay = BasePath + "/live-relay";
    public const string NotifySocket = BasePath + "/notify/ws-token";
    public const string NotifySocketWasm = BasePath + "/notify/ws-token-wasm";
    public const string Links = BasePath + "/links";
    public const string Mail = BasePath + "/mail";
    public const string Plan = BasePath + "/plan";
    public const string Capi = BasePath + "/capi";

    // The owner console's management APIs, and what it publishes and reads alongside them
    public const string DriveManagement = BasePath + "/drive-management";
    public const string AppManagement = BasePath + "/app-management";
    public const string Configuration = BasePath + "/config";
    public const string YouAuthDomains = BasePath + "/youauth-domains";
    public const string StaticContent = BasePath + "/optimization/cdn";
    public const string PublicKeys = BasePath + "/public/keys";
    // Owner console: security, recovery, health and system
    public const string Security = BasePath + "/security";
    public const string SecurityZk = Security + "/zk";
    public const string SecurityRecovery = Security + "/recovery";
    public const string Dns = BasePath + "/dns";
    public const string MailOwner = Mail + "/owner";
    public const string DataConversion = BasePath + "/data-conversion";

    // Slug addressing: /apps/{appSlug}/drives/{driveSlug} (docs/security-model.md, Drive addressing). Rooted at
    // /apps rather than nested under /drives, because /drives/feed would read as "the drive named
    // feed" rather than "the feed app's drives".
    public const string AppsRoot = BasePath + "/apps";
    public const string ByAppSlug = AppsRoot + "/{appSlug}";
    public const string AppDrivesRoot = ByAppSlug + "/drives";
    public const string AppDriveBySlug = AppDrivesRoot + "/{driveSlug}";

    public const string PeerRoot = BasePath + "/peer";
    public const string PeerByOdinId = PeerRoot + "/{odinId}";
    public const string PeerDrivesRoot = PeerByOdinId + "/drives";
    public const string PeerByDriveId = PeerDrivesRoot + "/{driveId:guid}";
    public const string PeerAuth = PeerByOdinId + "/auth";
    public const string PeerFilesRoot = PeerByDriveId + "/files";
    public const string PeerByFileId = PeerFilesRoot + "/{fileId:guid}";
    public const string PeerByUniqueId = PeerFilesRoot + "/by-uid/{uid:guid}";
    public const string PeerByGtid = PeerFilesRoot + "/by-gtid/{gtid:guid}";
    public const string PeerReactionsByGtid = PeerByGtid + "/reactions";

    // Temporal (time-boxed) read of a drive hosted by another identity. Parallels the peer file-read
    // chain above so temporal actions share the same templates as their non-temporal twins.
    public const string PeerTemporalRoot = PeerByDriveId + "/temporal";
    public const string PeerTemporalFilesRoot = PeerTemporalRoot + "/files";
    public const string PeerTemporalByFileId = PeerTemporalFilesRoot + "/{fileId:guid}";

    // Slug addressing over peer: /peer/{odinId}/apps/{appSlug}/drives/{driveSlug} -- the portable
    // name, and the reason the slug columns exist.  Resolved by the recipient, exactly as a drive id
    // is: the sender asks the remote what the address names there, then uses the ordinary peer path.
    public const string PeerByAppSlug = PeerByOdinId + "/apps/{appSlug}";
    public const string PeerAppDriveBySlug = PeerByAppSlug + "/drives/{driveSlug}";
    public const string PeerAppFilesRoot = PeerAppDriveBySlug + "/files";
    public const string PeerAppByFileId = PeerAppFilesRoot + "/{fileId:guid}";
    public const string PeerAppByUniqueId = PeerAppFilesRoot + "/by-uid/{uid:guid}";
    public const string PeerAppByGtid = PeerAppFilesRoot + "/by-gtid/{gtid:guid}";

    public const string PeerAppTemporalRoot = PeerAppDriveBySlug + "/temporal";
    public const string PeerAppTemporalFilesRoot = PeerAppTemporalRoot + "/files";
    public const string PeerAppTemporalByFileId = PeerAppTemporalFilesRoot + "/{fileId:guid}";

    // Peer notifications (subscribe to live updates on a drive hosted by another identity)
    public const string PeerNotifyRoot = PeerRoot + "/notify";
    public const string PeerSubscriptions = PeerNotifyRoot + "/subscriptions";
    public const string PeerNotifySocket = PeerNotifyRoot + "/ws-token";
    public const string PeerNotifySocketWasm = PeerNotifyRoot + "/ws-token-wasm";

    // Cookie-authenticated notification sockets for browsers (the owner console, apps with no bearer token).
    // One route per cookie, as V1 has one per API: an app served from the identity's own host sends the owner
    // cookie too, so the route, not the cookies present, says which caller this is.
    public const string NotifySocketOwnerCookie = BasePath + "/notify/ws-cookie-owner";
    public const string NotifySocketAppCookie = BasePath + "/notify/ws-cookie-app";
    public const string NotifyPreAuth = BasePath + "/notify/preauth";
    // Sign-in. The controllers under SignIn/ are V1's own with these routes; see docs/youauth-unified-authorization.md,
    // "The V2 routes", for why they keep V1's authentication schemes.
    public const string Ident = Auth + "/ident";
    public const string OwnerAuth = Auth + "/owner";
    public const string YouAuth = BasePath + "/youauth";
    public const string YouAuthAuthorize = YouAuth + "/authorize";
    public const string YouAuthToken = YouAuth + "/token";
    public const string Home = BasePath + "/home";
    public const string HomeAuth = Home + "/auth";
    public const string HomeCacheable = Home + "/data/cacheable";
}
