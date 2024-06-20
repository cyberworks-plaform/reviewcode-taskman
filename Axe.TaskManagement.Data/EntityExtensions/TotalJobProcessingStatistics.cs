using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class TotalJobProcessingStatistics
    {
        public Guid UserInstanceId { get; set; }
        public string SegmentLabeling { get; set; }
        public string DataEntryProcess { get; set; }
        public string DataEntryProcessIsIgnore { get; set; }
        public string DataEntryProcessInput { get; set; }
        public string DataCheckCorrect { get; set; }
        public string DataCheckWrong { get; set; }
        public string DataCheck { get; set; }
       
        public string DataConfirm { get; set; }

        public string DataCheckFinal { get; set; }
        public string DataEntryBool { get; set; }
    }

    public class JobProcessingStatistics
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public Guid? UserInstanceId { get; set; }
        public string ActionCode { get; set; }
        public long Total { get; set; }
        public long Total_Ignore { get; set; }
        public long Total_Correct { get; set; }
        public long Total_Wrong { get; set; }
    }
}
