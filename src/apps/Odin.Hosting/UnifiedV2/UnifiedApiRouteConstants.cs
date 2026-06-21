namespace Odin.Hosting.UnifiedV2;

public static class UnifiedApiRouteConstants
{
    public const string BasePath = "/api/v2";
    public const string Auth = BasePath + "/auth";
    public const string Connections = BasePath + "/connections";
    public const string Contacts = BasePath + "/contacts";
    public const string Health = BasePath + "/health";
    public const string DrivesRoot = BasePath + "/drives";
    public const string ByDriveId = DrivesRoot + "/{driveId:guid}";
    public const string FilesRoot = ByDriveId + "/files";
    public const string ByFileId = FilesRoot + "/{fileId:guid}";
    public const string ReactionsByFileId = FilesRoot + "/{fileId:guid}/reactions";
    public const string GroupReactionsByFileId = FilesRoot + "/{fileId:guid}/group-reactions";
    public const string ByUniqueId = FilesRoot + "/by-uid/{uid:guid}";
    public const string Notify = BasePath + "/notify/push";
    public const string LiveRelay = BasePath + "/live-relay";
    public const string NotifySocket = BasePath + "/notify/ws-token";
    public const string NotifySocketWasm = BasePath + "/notify/ws-token-wasm";
    public const string Links = BasePath + "/links";
    public const string Capi = BasePath + "/capi";

    public const string PeerRoot = BasePath + "/peer";
    public const string PeerByOdinId = PeerRoot + "/{odinId}";
    public const string PeerByDriveId = PeerByOdinId + "/drives/{driveId:guid}";
    public const string PeerFilesRoot = PeerByDriveId + "/files";
    public const string PeerByFileId = PeerFilesRoot + "/{fileId:guid}";
    public const string PeerByUniqueId = PeerFilesRoot + "/by-uid/{uid:guid}";
    public const string PeerByGtid = PeerFilesRoot + "/by-gtid/{gtid:guid}";

    // Temporal (time-boxed) read of a drive hosted by another identity. Parallels the peer file-read
    // chain above so temporal actions share the same templates as their non-temporal twins.
    public const string PeerTemporalRoot = PeerByDriveId + "/temporal";
    public const string PeerTemporalFilesRoot = PeerTemporalRoot + "/files";
    public const string PeerTemporalByFileId = PeerTemporalFilesRoot + "/{fileId:guid}";

    // Peer notifications (subscribe to live updates on a drive hosted by another identity)
    public const string PeerNotifyRoot = PeerRoot + "/notify";
    public const string PeerSubscriptions = PeerNotifyRoot + "/subscriptions";
    public const string PeerNotifySocket = PeerNotifyRoot + "/ws-token";
    public const string PeerNotifySocketWasm = PeerNotifyRoot + "/ws-token-wasm";
}
