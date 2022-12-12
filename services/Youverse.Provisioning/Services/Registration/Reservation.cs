namespace Youverse.Provisioning.Services.Registration
{
    public class ReservationResponse
    {
        public Guid Id { get; set; }

        public string Domain { get; set; }

        public Guid DomainKey { get; set; }

        public UInt64 CreatedTime { get; set; }

        public UInt64 ExpiresTime { get; set; }
    }
}