namespace Youverse.Hosting.Tests
{
    public class TransitTestUtilsOptions
    {
        public static TransitTestUtilsOptions Default = new TransitTestUtilsOptions()
        {
            ProcessOutbox = false,
            ProcessTransitBox = false
        };

        /// <summary>
        /// Indicates if the process outbox endpoint should be called after sending a transfer
        /// </summary>
        public bool ProcessOutbox { get; set; }

        /// <summary>
        /// Indicates if the process incoming transfers should be called after sending a transfer
        /// </summary>
        public bool ProcessTransitBox { get; set; }
    }
}