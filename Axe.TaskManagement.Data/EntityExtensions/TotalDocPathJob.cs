using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class TotalDocPathJob
    {
        public Guid BatchJobInstanceId { get; set; }
        public string BatchName { get; set; }
        public int NumOfRound { get; set; }
        public string Path { get; set; }
        public int Total { get; set; }
        public short Status { get; set; }
        public string ActionCode { get; set; }
        public Guid WorkflowStepInstanceId { get; set; }
        public Guid DocInstanceId { get; set; }
        public long? SyncTypeId { get; set; }
        public Guid? SyncTypeInstanceId { get; set; }
        public Guid ProjectInstanceId { get; set; }
    }
    public class SummaryTotalDocPathJob
    {
        public long PathId { get; set; }
        public string PathRelation { get; set; }
        public string SyncMetaValuePath { get; set; }
        public List<TotalDocPathJob> data { get; set; }
    }

    public class SummaryDoc
    {
        public string Path { get; set; }
        public List<TotalDocPathJob> SummaryFile { get; set; }
    }
}
