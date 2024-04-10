using System;
using MediatR;
using Odin.Services.Base;

namespace Odin.Services.Mediator;

public abstract class MediatorNotificationBase(OdinContext context) : EventArgs, INotification
{
    /// <summary>
    /// The context which raised the notification
    /// </summary>
    public OdinContext Context { get; init; } = context;
}