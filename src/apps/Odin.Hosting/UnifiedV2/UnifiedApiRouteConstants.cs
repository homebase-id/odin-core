namespace Odin.Hosting.UnifiedV2;

public static class UnifiedApiRouteConstants
{
    public const string BasePath = "/api/v2";
    public const string Auth = BasePath + "/auth";
    public const string Health = BasePath + "/health";
    public const string DrivesRoot = BasePath + "/drives";
    public const string ByDriveId = DrivesRoot + "/{driveId:guid}";
    public const string FilesRoot = ByDriveId + "/files";
    public const string ByFileId = FilesRoot + "/{fileId:guid}";
    public const string ReactionsByFileId = FilesRoot + "/{fileId:guid}/reactions";
    public const string ByUniqueId = FilesRoot + "/by-uid/{uid:guid}";

    public const string Notify = BasePath + "/notify/push";

}