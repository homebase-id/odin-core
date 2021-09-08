namespace DotYou.DigitalIdentityHost.Controllers.Perimeter.Xfer
{
    /// <summary>
    /// A reply from sending a payload to another Digital Identity
    /// </summary>
    public class Reply
    {
        /// <summary>
        /// Indicates if the payload was successfully received
        /// </summary>
        public bool Success { get; set; }

        public FailureReason FailureReason { get; set; } = FailureReason.None;
    }
}