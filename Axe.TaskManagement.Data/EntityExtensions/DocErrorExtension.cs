using System;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class DocErrorExtension
    {
        public Guid DocInstanceId { get; set; }

        public string DocName { get; set; }

        public string DocPath { get; set; }

        public DateTime? DocCreatedDate { get; set; }

        public Guid? WorkflowStepInstanceId { get; set; }

        public string WorkflowStepName { get; set; }

        public string ActionCode { get; set; }

        public DateTime? LastModificationDate { get; set; }

        public short RetryCount { get; set; }

        public int TenantId { get; set; }
    }
}
