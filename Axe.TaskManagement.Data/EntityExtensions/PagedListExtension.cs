using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Data.EntityExtensions
{
    public class PagedListExtension<T> : PagedList<T>
    {
        public long TotalCorrect
        {
            get;
            set;
        }
        public long TotalComplete
        {
            get;
            set;
        }
        public long TotalWrong
        {
            get;
            set;
        }
        public long TotalIsIgnore
        {
            get;
            set;
        }
        public long TotalError
        {
            get;
            set;
        }
        public List<JobProcessingStatistics> lstJobProcessingStatistics { get; set; } = new List<JobProcessingStatistics>();
    }
}
