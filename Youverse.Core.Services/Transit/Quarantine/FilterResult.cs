namespace Youverse.Core.Services.Transit.Quarantine
{
    public class AddPartResponse
    {
         
    }
    
    public enum FilterResult
    {
        /// <summary>
        /// Specifies handling the file part was ok and we should continue receiving the other file parts
        /// </summary>
        ShouldContinue = 2,

        /// <summary>
        /// Specifies you should abort receiving the file as it is considered dangerous
        /// </summary>
        ShouldAbortDangerousPayload = 3
    }
}