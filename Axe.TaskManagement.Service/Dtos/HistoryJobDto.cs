using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Service.Dtos
{
    public  class HistoryJobDto
    {
        //Contructor

        public HistoryJobDto(PagedList<JobDto> pagedList, int countAbnormalJob)
        {
            PagedList = pagedList;
            TotalJob = pagedList?.TotalFilter ?? 0;
            CountAbnormalJob = countAbnormalJob;
            CountNormalJob = TotalJob - CountAbnormalJob;
        }

        //Prop
        public PagedList<JobDto> PagedList { get; set; }

        public int TotalJob { get; set; }
        public int CountNormalJob { get; set; }
        public int CountAbnormalJob { get; set; }
    }
}
