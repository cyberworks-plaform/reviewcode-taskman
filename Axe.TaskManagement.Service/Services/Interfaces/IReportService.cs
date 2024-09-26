using Axe.TaskManagement.Service.Dtos;
using Ce.Common.Lib.Abstractions;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IReportService
    {
        Task<byte[]> ExportExcelHistoryJobByUser(PagingRequest request, string actionCode, string accessToken);

        Task<byte[]> ExportExcelHistoryJobByUserV2(PagingRequest request, string actionCode, string accessToken);
    }
}
