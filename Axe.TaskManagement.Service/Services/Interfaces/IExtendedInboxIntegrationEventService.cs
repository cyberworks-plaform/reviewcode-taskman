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
    public interface IExtendedInboxIntegrationEventService
    {
        Task<GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>> GetPagingAsync(PagingRequest request);
        Task<GenericResponse<ExtendedInboxIntegrationEventDto>> GetByIntergrationEventIdAsync(Guid intergrationEventId);
        Task<GenericResponse<IEnumerable<ExtendedInboxIntegrationEventDto>>> GetByIdsAsync(string ids);
        Task<GenericResponse<long>> TotalCountAsync();
    }
}
