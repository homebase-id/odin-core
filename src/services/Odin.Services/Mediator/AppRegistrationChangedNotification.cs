using System;
using MediatR;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Services.Mediator;

public class AppRegistrationChangedNotification(OdinContext context) : MediatorNotificationBase(context)
{
    public AppRegistration NewAppRegistration { get; set; }
    public AppRegistration OldAppRegistration { get; set; }
}