namespace Youverse.Core.Services.Drives.DriveCore.Query
{
    public enum IndexReadyState
    {
        RequiresRebuild = 0,
        Ready = 2,
        IsRebuilding = 3
    }
}