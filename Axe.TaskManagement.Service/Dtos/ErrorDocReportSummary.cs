using System;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ErrorDocReportSummary 
    {
        public Guid InstanceId { get; set; }

        public string ActionCode { get; set; }

        public string WorkflowStepName { get; set; }

        public Guid WorkflowStepInstanceId { get; set; }
    }
}
