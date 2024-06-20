using Ce.EventBus.Lib.Events;
using System;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class ProjectStatisticUpdateProgressEvent : IntegrationEvent
    {
        public Guid? ProjectTypeInstanceId { get; set; }

        public Guid ProjectInstanceId { get; set; }

        public Guid? WorkflowInstanceId { get; set; }

        public Guid? WorkflowStepInstanceId { get; set; }

        public string ActionCode { get; set; }

        public Guid DocInstanceId { get; set; }

        public int StatisticDate { get; set; }

        public string ChangeFileProgressStatistic { get; set; }

        public string ChangeStepProgressStatistic { get; set; }

        public string ChangeUserStatistic { get; set; }

        public int TenantId { get; set; }

        public string AccessToken { get; set; }
    }
}
