using Axe.TaskManagement.Model.Entities;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Service.Dtos
{
    public  class HistoryComplainDto
    {
        //Contructor

        public HistoryComplainDto(PagedList<ComplainDto> pagedList)
        {
            PagedList = pagedList;
        }

        //Prop
        public PagedList<ComplainDto> PagedList { get; set; }
    }
}
