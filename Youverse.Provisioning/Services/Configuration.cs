namespace Youverse.Provisioning.Services
{
    public static class Configuration
    {
        /// <summary>
        /// Number of seconds a reservation should be held
        /// </summary>
        /// <returns></returns>
        public static int ReservationLength { get; } = 60 * 30;
    }
}