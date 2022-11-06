namespace Youverse.Provisioning.Services.Registration
{
    public class ReservationFailedException : Exception
    {
        public ReservationFailedException()
        {
        }

        public ReservationFailedException(string message)
            : base(message)
        {
        }

        public ReservationFailedException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}