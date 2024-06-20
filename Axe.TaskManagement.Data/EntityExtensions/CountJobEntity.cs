using Axe.Utility.EntityExtensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class CountJobEntity
    {
        public Axe.Utility.Enums.EnumJob.Status Status { get; set; }
        public string StatusName { get; set; }
        public int Complete { get; set; }
        public int Percent { get; set; }
        public int Total { get; set; }
        public string ActionCode { get; set; }
        public Guid? WorkflowStepInstanceId { get; set; }
        public Guid? DocInstanceId { get; set; }

    }

    public class WfStepOrder
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public List<WorkflowStepInfo> wfStepBefore { get; set; }
    }

    public class DocByWfStep
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public List<Guid?> DocInstanceIds { get; set; }
    }

    public class WfStep
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public string ActionCode { get; set; }
    }

    public class WorkSpeedDetailEntity
    {
        public Guid? UserInstanceId { get; set; }
        public Guid? TurnInstanceId { get; set; }
        public string ActionCode { get; set; }
        public int TotalJob { get; set; }
        public int TotalTime { get; set; }
        public int WorkSpeed { get; set; }
    }

    public class WorkSpeedTotalEntity
    {
        public string ActionCode { get; set; }
        public int TotalJob { get; set; }
        public int TotalTime { get; set; }
        public int WorkSpeed { get; set; }
    }

    public class WorkSpeedReportEntity
    {
        public List<WorkSpeedTotalEntity> WorkSpeedTotal { get; set; }
        public List<WorkSpeedDetailEntity> WorkSpeedDetail { get; set; }
    }


    public class JobEntity
    {
        public Guid? UserInstanceId { get; set; }
        public Guid? TurnInstanceId { get; set; }
        public string ActionCode { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public DateTime? LastModificationDate { get; set; }

    }

    public class DocItemEntity
    {
        public Guid? DocInstanceId { get; set; }
        public string DocName { get; set; }
    }

    public class JobByDocDoneEntity
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public string ActionCode { get; set; }
        public int JobCount { get; set; }
        public int TotalJob { get; set; }
        public int Percent { get; set; }
    }

    public class JobOfFileEntity
    {
        public Guid? WorkflowStepInstanceId { get; set; }
        public string ActionCode { get; set; }
        public string Unit { get; set; }
        public string ErrorDetail { get; set; }
        public int TotalJob { get; set; }
        public int WaitingJob { get; set; }
        public int ProcessingJob { get; set; }
        public int ErrorJob { get; set; }
        public int CompleteJob { get; set; }
        public int IgnoreJob { get; set; }
    }
}
