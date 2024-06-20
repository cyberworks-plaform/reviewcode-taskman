using Ce.EventBus.Lib.Events;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class ProjectStatisticUpdateMultiProgressEvent : IntegrationEvent
    {
        public List<ItemProjectStatisticUpdateProgress> ItemProjectStatisticUpdateProgresses { get; set; }

        public Guid? ProjectTypeInstanceId { get; set; }

        public Guid ProjectInstanceId { get; set; }

        public Guid? WorkflowInstanceId { get; set; }

        public Guid? WorkflowStepInstanceId { get; set; }

        public string ActionCode { get; set; }

        public string DocInstanceIds { get; set; }

        public int TenantId { get; set; }

        public string AccessToken { get; set; }
    }
}
