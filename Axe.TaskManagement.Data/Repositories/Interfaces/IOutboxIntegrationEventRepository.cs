using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.DapperBase.Interfaces;
using Ce.Constant.Lib.Dtos;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Interfaces;

public interface IOutboxIntegrationEventRepository : IDapperBaseRepository<OutboxIntegrationEvent, long>
{
    Task<IEnumerable<OutboxIntegrationEvent>> GetOutboxIntegrationEvent();

    Task<IEnumerable<OutboxIntegrationEvent>> GetOutboxIntegrationEventV2();
    Task<Dictionary<int, long>> GetTotalAndStatusCountAsync();
    Task<PagedList<OutboxIntegrationEvent>> GetPagingCusAsync(PagingRequest request, CommandType commandType = CommandType.Text);
}