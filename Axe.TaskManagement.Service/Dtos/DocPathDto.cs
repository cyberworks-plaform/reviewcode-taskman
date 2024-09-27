using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Dtos
{
    public class DocPathDto
    {
        public long SyncTypeId { get; set; }
        public Guid SyncTypeInstanceId { get; set; }


        public string SyncMetaRelationPath { get; set; }
        public string SyncMetaIdPath { get; set; }
        public List<long> LstSyncMetaRelationId { get; set; } = new List<long>();
        public List<long> LstSyncMetaId { get; set; } = new List<long>();
        public List<string> LstSyncMetaValue { get; set; } = new List<string>();

        public int Level { get; set; }
        public long SyncMetaRelationId { get; set; } = 0;
        public long ParentSyncMetaRelationId { get; set; } = 0;
        public string SyncMetaValue { get; set; }
        public string SyncMetaValuePath { get; set; }

        public bool IsContentFile { get; set; } = false;
        public bool Islocked { get; set; } = false;
        public bool IsPriorited { get; set; } = false;
        public int ProjectId { get; set; }
        public int TotalRow { get; set; } = 0;

    }
}
