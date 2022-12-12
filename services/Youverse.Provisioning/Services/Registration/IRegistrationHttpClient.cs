using Refit;

namespace Youverse.Provisioning.Services.Registration;

public interface IRegistrationHttpClient
{
    [Get("/availability/{domain}")]
    public Task<ApiResponse<bool>> IsAvailable(string domain);

    [Post("/reservations")]
    public Task<ApiResponse<ReservationResponse>> Reserve([Body] ReservationRequest request);

    [Delete("/reservations/{reservationId}")]
    Task<ApiResponse<HttpContent>> CancelReservation(Guid reservationId);

    [Post("/")]
    Task<ApiResponse<Guid>> Register(RegistrationInfo info);

    [Get("/status")]
    Task<ApiResponse<RegistrationStatus>> GetStatus(Guid firstRunToken);
}