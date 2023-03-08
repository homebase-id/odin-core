namespace Youverse.Provisioning.Services.Registration;

public class CancelReservationRequest
{
    public string DomainName { get; set; } = "";
    public Guid ReservationId { get; set; }
}