using Ce.EventBus.Lib.Events;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class DocChangeDeleteableEvent : IntegrationEvent
    {
        public List<Guid> DocInstanceIds { get; set; }
    }
}
