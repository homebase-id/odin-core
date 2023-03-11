namespace Youverse.Core.Services.Transit.ReceivingHost.Quarantine
{
    /// <summary>
    /// The action recommended by the filter
    /// </summary>
    public enum FilterAction
    {
        None = 0,
        Quarantine = 1,
        Reject = 2,
        Accept = 5
    }
}