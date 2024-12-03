using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.DapperBase.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Ce.Common.Lib.Abstractions;
using Ce.Constant.Lib.Dtos;
using System.Data;

namespace Axe.TaskManagement.Data.Repositories.Interfaces;

public interface IExtendedInboxIntegrationEventRepository : IDapperBaseRepository<ExtendedInboxIntegrationEvent, Guid>
{
    Task<Tuple<bool, ExtendedInboxIntegrationEvent>> TryInsertInbox(ExtendedInboxIntegrationEvent entity);
    Task<PagedList<ExtendedInboxIntegrationEvent>> GetPagingCusAsync(PagingRequest request, CommandType commandType = CommandType.Text);
    Task<ExtendedInboxIntegrationEvent> GetByKeyAsync(Guid intergrationEventId, string serviceCode);

    Task<ExtendedInboxIntegrationEvent> GetByIntergrationEventIdAsync(Guid intergrationEventId);

    Task<ExtendedInboxIntegrationEvent> GetInboxIntegrationEventAsync(short maxRetry);

    Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsInboxIntegrationEventAsync(int batchSize, short maxRetry);

    Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsRecallInboxIntegrationEventAsync(int maxMinutesAllowedProcessing);
    Task<Dictionary<int, long>> GetTotalAndStatusCountAsync();

    Task<int> UpdateMultiPriorityAsync(string serviceCode, string exchangeName, Guid projectInstanceId, short priority, int batchSize = 100);
}