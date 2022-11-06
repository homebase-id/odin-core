using System.Collections.Immutable;

namespace Youverse.Provisioning.Services.Certificate
{
    public class CertificateOrderList
    {
        private readonly Dictionary<Guid, PendingCertificateOrder> _orders;

        public CertificateOrderList()
        {
            _orders = new Dictionary<Guid, PendingCertificateOrder>();
        }

        public ImmutableDictionary<Guid, PendingCertificateOrder> GetOrders(CertificateOrderStatus status)
        {
            return _orders.Where(o => o.Value.Status == status).ToImmutableDictionary();
        }
        
        /// <summary>
        /// Adds a certificate order to be watched by the <see cref="LetsEncryptCertificateOrderStatusChecker"/>.  Returns the Id to lookup the status.
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public void AddCertificateOrder(Guid orderId, PendingCertificateOrder order)
        {
            //TODO: need locking?
            _orders.Add(orderId, order);
        }

        public bool IsCertificateReady(Guid key)
        {
            return _orders.TryGetValue(key, out var order) && order.Status == CertificateOrderStatus.Verified;
        }

        public PendingCertificateOrder GetAndRemove(Guid key)
        {
            //TODO: need locking?
            _orders.Remove(key, out var order);
            return order;
        }

        public void MarkVerified(Guid key)
        {
            _orders[key].Status = CertificateOrderStatus.Verified;
        }
        
        public void MarkVerificationFailed(Guid key)
        {
            _orders[key].Status = CertificateOrderStatus.VerificationFailed;
        }

        public void MarkAwaitingVerification(Guid key)
        {
            _orders[key].Status = CertificateOrderStatus.AwaitingVerification;
        }

        public PendingCertificateOrder Get(Guid orderId)
        {
            if (_orders.TryGetValue(orderId, out var result))
            {
                return result;
            }

            return null;
        }
    }
}