using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Youverse.Core.Storage;
using Youverse.Core.Storage.Sqlite.IdentityDatabase;
using Youverse.Core.Util;

#nullable enable

namespace Youverse.Core.Services.Registry.Registration;

public class ReservationStorage
{
    private readonly ConcurrentDictionary<Guid, Reservation> _reservations;

    public ReservationStorage()
    {
        _reservations = new ConcurrentDictionary<Guid, Reservation>();
    }

    public Reservation GetByDomain(string domain)
    {
        var reservation = _reservations.Values.SingleOrDefault(v => v.DomainKey == GetDomainKey(domain));
        return reservation;
    }

    public Reservation Get(Guid reservationId)
    {
        if (_reservations.TryGetValue(reservationId, out var reservation))
        {
            return reservation;
        }

        return null;
    }

    public void Delete(Guid reservationId)
    {
        var _ = _reservations.TryRemove(reservationId, out var _);
    }

    public void Save(Reservation reservation)
    {
        _reservations.AddOrUpdate(reservation.Id, reservation, (guid, existingReservation) => reservation);
    }

    private Guid GetDomainKey(string domain)
    {
        return HashUtil.ReduceSHA256Hash(domain);
    }
}