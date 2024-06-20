using System;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class ItemProjectStatisticUpdateProgress
    {
        public Guid DocInstanceId { get; set; }

        public int StatisticDate { get; set; }

        public string ChangeFileProgressStatistic { get; set; }

        public string ChangeStepProgressStatistic { get; set; }

        public string ChangeUserStatistic { get; set; }
    }
}
