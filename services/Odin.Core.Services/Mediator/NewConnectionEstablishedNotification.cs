using System;
using MediatR;
using Odin.Core.Identity;

namespace Odin.Core.Services.Mediator
{
    /// <summary>
    /// Indicates a new connection was established with the Identity
    /// </summary>
    public class NewConnectionEstablishedNotification : INotification
    {
        public Guid NotificationTypeId { get; } = Guid.Parse("8208e156-6175-42ad-af13-e462fcefc85a");
        public OdinId OdinId { get; set; }
    }
}