// using System;
// using System.Collections.Generic;
// using System.Collections.Immutable;
// using System.Linq;
//
// namespace Youverse.Core.Services.Certificate.Renewal
// {
//     public class CertificateOrderList
//     {
//         private readonly Dictionary<Guid, PendingCertificateOrder> _orders;
//
//         public CertificateOrderList()
//         {
//             _orders = new Dictionary<Guid, PendingCertificateOrder>();
//         }
//
//         public ImmutableDictionary<Guid, PendingCertificateOrder> GetOrders(CertificateOrderStatus status)
//         {
//             return _orders.Where(o => o.Value.Status == status).ToImmutableDictionary();
//         }
//
//         public bool IsCertificateReady(Guid key)
//         {
//             return _orders.TryGetValue(key, out var order) && order.Status == CertificateOrderStatus.Verified;
//         }
//
//         public PendingCertificateOrder GetAndRemove(Guid key)
//         {
//             //TODO: need locking?
//             _orders.Remove(key, out var order);
//             return order;
//         }
//
//         public void MarkVerified(Guid key)
//         {
//             _orders[key].Status = CertificateOrderStatus.Verified;
//         }
//         
//         public void MarkVerificationFailed(Guid key)
//         {
//             _orders[key].Status = CertificateOrderStatus.VerificationFailed;
//         }
//
//         public void MarkAwaitingVerification(Guid key)
//         {
//             _orders[key].Status = CertificateOrderStatus.AwaitingVerification;
//         }
//
//         public PendingCertificateOrder Get(Guid orderId)
//         {
//             if (_orders.TryGetValue(orderId, out var result))
//             {
//                 return result;
//             }
//
//             return null;
//         }
//     }
// }