using Ce.EventBus.Lib.Events;
using System;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class DocUpdateFinalValueEvent : IntegrationEvent
    {
        public Guid DocInstanceId { get; set; }

        public string FinalValue { get; set; }

        public short Status { get; set; }
    }
}
