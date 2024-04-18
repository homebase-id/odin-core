using System;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Services.Base;

namespace Odin.Services.Mediator;

public class MediatorNotificationBase : EventArgs, INotification
{
    protected MediatorNotificationBase()
    {
        if (null == OdinContext)
        {
            throw new OdinSystemException("Context not set on mediator event");
        }
    }

    /// <summary>
    /// The context at the time the event was raised
    /// </summary>
    public OdinContext OdinContext { get; init; }
}