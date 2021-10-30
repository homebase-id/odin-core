namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// The action recommended by the filter
    /// </summary>
    public enum FilterAction
    {
        Accept = 0,
        Quarantine = 1,
        Reject = 2,
    }
}