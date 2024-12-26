using Axe.TaskManagement.Data.Repositories.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.DapperBase.Implementations;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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
        public async Task<Dictionary<int, long>> GetTotalAndStatusCountAsync()
        {
            var result = new Dictionary<int, long>();

            string sql = $"SELECT \"Status\", COUNT(*) AS Count FROM {_tableName} GROUP BY \"Status\"; SELECT COUNT(*) AS Total FROM {_tableName};";
            using (var multi = await _conn.QueryMultipleAsync(sql, commandType: CommandType.Text))
            {
                var statusCounts = await multi.ReadAsync<(int Status, long Count)>();
                foreach (var item in statusCounts)
                {
                    result[item.Status] = item.Count;
                }

                var total = await multi.ReadSingleAsync<long>();

                result[-1] = total; //Total lưu tạm vào -1 
            }

            return result;
        }

        public async Task<PagedList<OutboxIntegrationEvent>> GetPagingCusAsync(PagingRequest request, CommandType commandType = CommandType.Text)
        {

            string sqlWhere = GenerateWhereClause(request.Filters);

            string whereClause = string.Empty;
            if (!string.IsNullOrEmpty(sqlWhere) && sqlWhere != "()")
            {
                whereClause = $" WHERE {sqlWhere}";
            }
            string sqlOrderBy = GenerateOrderClause(request.Sorts);
            string orderByClause = string.IsNullOrEmpty(sqlOrderBy) ? string.Empty : $"ORDER BY {sqlOrderBy}";

            string selectClause = GenerateSelectClause(request.Fields);

            int skip = (request.PageInfo.PageIndex - 1) * request.PageInfo.PageSize;
            var parameters = new DynamicParameters();
            parameters.Add("@PageSize", request.PageInfo.PageSize, DbType.Int32);
            parameters.Add("@Skip", skip, DbType.Int32);

            var query = $"SELECT {selectClause} FROM {_tableName} {whereClause} {orderByClause} Limit @PageSize Offset @Skip;";
            var queryTotalCount = $"SELECT COUNT(*) FROM {_tableName};";
            var queryTotalFilter = $"SELECT COUNT(*) FROM {_tableName} {whereClause};";
            query = $"{query} {queryTotalCount} {queryTotalFilter}";
            var multi = await _conn.QueryMultipleAsync(query, parameters, commandType: commandType);

            var data = multi.Read<OutboxIntegrationEvent>();
            var total = multi.Read<long>().FirstOrDefault();
            int totalfilter = multi.Read<int>().FirstOrDefault();

            return new PagedList<OutboxIntegrationEvent>
            {
                Data = data.ToList(),
                PageIndex = request.PageInfo.PageIndex,
                PageSize = request.PageInfo.PageSize,
                TotalCount = total,
                TotalFilter = totalfilter,
                TotalPages = (int)Math.Ceiling((decimal)totalfilter / request.PageInfo.PageSize)
            };
        }
    }
}
