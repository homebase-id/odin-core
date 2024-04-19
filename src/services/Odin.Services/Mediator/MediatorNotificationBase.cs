using System;
using MediatR;
using Odin.Services.Base;

namespace Odin.Services.Mediator;

public class MediatorNotificationBase : EventArgs, INotification
{
    /// <summary>
    /// The context at the time the event was raised
    /// </summary>
    public IOdinContext OdinContext { get; init; }
}