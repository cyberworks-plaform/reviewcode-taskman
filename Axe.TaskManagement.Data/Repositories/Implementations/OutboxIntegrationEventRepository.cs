using Axe.TaskManagement.Data.Repositories.Interfaces;
using Ce.Common.Lib.DapperBase.Implementations;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class OutboxIntegrationEventRepository : DapperBaseRepository<OutboxIntegrationEvent, long>, IOutboxIntegrationEventRepository
    {
        #region Initialize

        private readonly IDbConnection _conn;
        private const int BatchPublish = 1000;

        public OutboxIntegrationEventRepository(IDbConnection conn) : base(conn)
        {
            _conn = (_conn ?? (IDbConnection) conn);
        }

        #endregion

        public async Task<IEnumerable<OutboxIntegrationEvent>> GetOutboxIntegrationEvent()
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryAsync<OutboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(OutboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.PublishMessageStatus.Pending} OR \"{nameof(OutboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.PublishMessageStatus.Nack} ORDER BY \"{nameof(OutboxIntegrationEvent.Status)}\" LIMIT {BatchPublish}");
            }

            return await _conn.QueryAsync<OutboxIntegrationEvent>(
                $"SELECT TOP ({BatchPublish}) * FROM {_tableName} WHERE {nameof(OutboxIntegrationEvent.Status)} = {(short)EnumEventBus.PublishMessageStatus.Pending} OR {nameof(OutboxIntegrationEvent.Status)} = {(short)EnumEventBus.PublishMessageStatus.Nack} ORDER BY {nameof(OutboxIntegrationEvent.Status)}");
        }

        public async Task<IEnumerable<OutboxIntegrationEvent>> GetOutboxIntegrationEventV2()
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryAsync<OutboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(OutboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.PublishMessageStatus.Nack} ORDER BY \"{nameof(OutboxIntegrationEvent.Status)}\" LIMIT {BatchPublish}");
            }

            return await _conn.QueryAsync<OutboxIntegrationEvent>(
                $"SELECT TOP ({BatchPublish}) * FROM {_tableName} WHERE {nameof(OutboxIntegrationEvent.Status)} = {(short)EnumEventBus.PublishMessageStatus.Nack} ORDER BY {nameof(OutboxIntegrationEvent.Status)}");
        }
    }
}
