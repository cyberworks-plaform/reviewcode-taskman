using Axe.Utility.EntityExtensions;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Dtos
{
    public class DocCheckHasJobWaitingOrProcessingDto
    {
        public Guid DocInstanceId { get; set; }

        public List<WorkflowStepInfo> CheckWorkflowStepInfos { get; set; }

        public List<Guid?> IgnoreListDocTypeField {  get; set; }    
    }
}
