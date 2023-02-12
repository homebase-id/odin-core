namespace Youverse.Core.Services.Drive.Core.Query
{
    public enum IndexReadyState
    {
        RequiresRebuild = 0,
        Ready = 2,
        IsRebuilding = 3
    }
}