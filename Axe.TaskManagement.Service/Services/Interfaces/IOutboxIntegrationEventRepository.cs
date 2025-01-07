using Ce.Constant.Lib.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.DapperBase.Interfaces;
using System.Data;
using Axe.TaskManagement.Service.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IOutBoxIntegrationEventService
{
    Task<GenericResponse<PagedList<OutboxIntegrationEventDto>>> GetPagingAsync(PagingRequest request);
    Task<GenericResponse<long>> TotalCountAsync();
    Task<GenericResponse<Dictionary<int, long>>> GetTotalAndStatusCountAsync();
    Task<GenericResponse<OutboxIntegrationEventDto>> GetByIdAsync(long id);
}