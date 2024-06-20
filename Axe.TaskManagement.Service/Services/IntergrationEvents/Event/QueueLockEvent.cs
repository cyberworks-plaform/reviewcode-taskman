using Ce.EventBus.Lib.Events;
using System;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class QueueLockEvent : IntegrationEvent
    {
        public Guid? ProjectInstanceId { get; set; }

        public string DocPath { get; set; }

        public int TenantId { get; set; }

        public string AccessToken { get; set; }
    }
}
