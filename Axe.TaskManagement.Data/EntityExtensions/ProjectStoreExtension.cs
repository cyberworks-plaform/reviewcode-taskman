using System;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class ProjectStoreExtension
    {
        public Guid? ProjectTypeInstanceId { get; set; }

        public Guid ProjectInstanceId { get; set; }

        public Guid? WorkflowInstanceId { get; set; }

        public Guid? WorkflowStepInstanceId { get; set; }

        public int TenantId { get; set; }
    }
}
