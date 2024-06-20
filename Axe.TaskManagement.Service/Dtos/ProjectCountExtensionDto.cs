using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ProjectCountExtensionDto
    {
        public int ProjectId { get; set; }
        public Guid? ProjectInstanceId { get; set; }
        public string ActionCode { get; set; }
        public int TotalJob { get; set; }
        public string DocPath { get; set; }
        public Guid? WorkflowInstanceId { get; set; }
        public Guid? DocTypeFieldInstanceId { get; set; }
        public short InputType { get; set; }
    }
}
