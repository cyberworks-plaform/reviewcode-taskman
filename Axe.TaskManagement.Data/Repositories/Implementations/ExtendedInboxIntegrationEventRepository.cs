using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.DapperBase.Implementations;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class ExtendedInboxIntegrationEventRepository : DapperBaseRepository<ExtendedInboxIntegrationEvent, Guid>, IExtendedInboxIntegrationEventRepository
    {
        #region Initialize

        private readonly IDbConnection _conn;
        private const int BatchRecall = 100;
        private static string _virtualHost;

        public ExtendedInboxIntegrationEventRepository(IDbConnection conn, IConfiguration configuration) : base(conn)
        {
            _conn = (_conn ?? (IDbConnection)conn);
            if (string.IsNullOrEmpty(_virtualHost))
            {
                _virtualHost = configuration.GetValue("RabbitMq:ConnectionStrings:VirtualHost", "/");
            }
        }

        #endregion

        public override async Task<int> UpdateAsync(ExtendedInboxIntegrationEvent entity)
        {
            entity.LastModificationDate = DateTime.UtcNow;
            var updateQuery = GenerateInboxIntegrationEventUpdateQuery();
            return await _conn.ExecuteAsync(updateQuery, entity);
        }

        public override async Task<int> DeleteAsync(ExtendedInboxIntegrationEvent entity)
        {
            var deleteQuery = GenerateInboxIntegrationEventDeleteQuery();
            return await _conn.ExecuteAsync(deleteQuery, entity);
        }

        /// <summary>
        /// Item1 kiểu bool: Có xử lý hay không (HasBeenProcessed)
        /// Item2 kiểu InboxIntegrationEvent: Nếu Item1 = true (có xử lý) thì xử lý với inbox entity
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        public async Task<Tuple<bool, ExtendedInboxIntegrationEvent>> TryInsertInbox(ExtendedInboxIntegrationEvent entity)
        {
            var result = false;
            ExtendedInboxIntegrationEvent addInboxEntity = null;

            try
            {
                var existed = await GetByKeyAsync(entity.IntergrationEventId, entity.ServiceCode);
                if (existed == null)
                {

                    entity.VirtualHost = _virtualHost;

                    addInboxEntity = await AddAsyncV2(entity);
                    result = true;
                }
            }
            catch (PostgresException ex)
            {
                if (ex.SqlState == "23505")
                {
                    // Violation in unique constraint
                }
                else
                {
                    throw;
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2601)
                {
                    // Violation in unique index
                }
                else if (ex.Number == 2627)
                {
                    // Violation in unique constraint
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.StackTrace);
                throw;
            }

            return new Tuple<bool, ExtendedInboxIntegrationEvent>(result, addInboxEntity);
        }

        public async Task<PagedList<ExtendedInboxIntegrationEvent>> GetPagingCusAsync(PagingRequest request, CommandType commandType = CommandType.Text)
        {
            Guid? projectInstanceId = null;
            if (request.Filters != null && request.Filters.Count > 0)
            {
                if (request.Filters.Count == 1 && request.Filters[0].Filters != null && request.Filters[0].Filters.Count > 0)
                {
                    var projectFilter = request.Filters[0].Filters.Where(x => x.Field.Equals("ProjectInstanceId"));

                    if (projectFilter != null && projectFilter.Count() > 0)
                    {
                        try
                        {
                            projectInstanceId = Guid.Parse(projectFilter.FirstOrDefault().Value);
                        }
                        catch (Exception ex)
                        {
                        }
                    }
                    var anotherFilter = request.Filters[0].Filters.Where(x => !x.Field.Equals("ProjectInstanceId")).ToList(); //Lấy ra những filter khác
                    request.Filters[0].Filters = anotherFilter; // gán lại vào filter gốc
                }
            }
            string projectWhere = string.Empty;
            if (projectInstanceId != null)
            {
                projectWhere = $"\"ExtendedInboxIntegrationEvents\".\"{nameof(ExtendedInboxIntegrationEvent.ProjectInstanceId)}\" = '{projectInstanceId.ToString()}'";
            }
            string sqlWhere = GenerateWhereClause(request.Filters);

            string whereClause = string.Empty;
            if ((!string.IsNullOrEmpty(sqlWhere) && sqlWhere == "()" && !string.IsNullOrEmpty(projectWhere)) || (string.IsNullOrEmpty(sqlWhere) && !string.IsNullOrEmpty(projectWhere)))
            {
                whereClause = $" WHERE {projectWhere}";
            }
            else if (!string.IsNullOrEmpty(sqlWhere) && sqlWhere != "()" && !string.IsNullOrEmpty(projectWhere))
            {
                whereClause = $" WHERE {projectWhere} OR {sqlWhere}";
            }
            else if (!string.IsNullOrEmpty(sqlWhere) && sqlWhere != "()" && string.IsNullOrEmpty(projectWhere))
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

            var data = multi.Read<ExtendedInboxIntegrationEvent>();
            var total = multi.Read<long>().FirstOrDefault();
            int totalfilter = multi.Read<int>().FirstOrDefault();

            return new PagedList<ExtendedInboxIntegrationEvent>
            {
                Data = data.ToList(),
                PageIndex = request.PageInfo.PageIndex,
                PageSize = request.PageInfo.PageSize,
                TotalCount = total,
                TotalFilter = totalfilter,
                TotalPages = (int)Math.Ceiling((decimal)totalfilter / request.PageInfo.PageSize)
            };
        }

        public async Task<ExtendedInboxIntegrationEvent> GetByKeyAsync(Guid intergrationEventId, string serviceCode)
        {
            var parameters = new DynamicParameters();
            parameters.Add("@IntergrationEventId", intergrationEventId, DbType.Guid);
            parameters.Add("@ServiceCode", serviceCode, DbType.String);

            string query;
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                query =
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\" = @{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} AND \"{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}\" = @{nameof(ExtendedInboxIntegrationEvent.ServiceCode)} LIMIT 1";
            }
            else
            {
                query =
                    $"SELECT TOP(1) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} = @{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} AND {nameof(ExtendedInboxIntegrationEvent.ServiceCode)} = @{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}";
            }

            return await _conn.QueryFirstOrDefaultAsync<ExtendedInboxIntegrationEvent>(query, parameters);
        }

        public async Task<ExtendedInboxIntegrationEvent> GetByIntergrationEventIdAsync(Guid intergrationEventId)
        {
            if (_providerName == "Npgsql")
            {
                return await _conn.QuerySingleOrDefaultAsync<ExtendedInboxIntegrationEvent>("SELECT * FROM " + _tableName + $" WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\" =@IntergrationEventId", new
                {
                    IntergrationEventId = intergrationEventId
                });
            }

            return await _conn.QuerySingleOrDefaultAsync<ExtendedInboxIntegrationEvent>("SELECT * FROM " + _tableName + $" WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\" =@IntergrationEventId", new
            {
                IntergrationEventId = intergrationEventId
            });
        }

        public async Task<ExtendedInboxIntegrationEvent> GetInboxIntegrationEventAsync(short maxRetry)
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryFirstOrDefaultAsync<ExtendedInboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Received} OR (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND \"{nameof(ExtendedInboxIntegrationEvent.RetryCount)}\" < {maxRetry})) ORDER BY \"{nameof(ExtendedInboxIntegrationEvent.Priority)}\" DESC, \"{nameof(ExtendedInboxIntegrationEvent.Status)}\", \"{nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}\", \"{nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}\" NULLS FIRST LIMIT 1");
            }

            return await _conn.QueryFirstOrDefaultAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP (1) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Received} OR ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND {nameof(ExtendedInboxIntegrationEvent.RetryCount)} < {maxRetry})) ORDER BY {nameof(ExtendedInboxIntegrationEvent.Priority)} DESC, {nameof(ExtendedInboxIntegrationEvent.Status)}, {nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}, (CASE WHEN {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)} IS NULL THEN 0 ELSE 1 END), {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}");
        }

        public async Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsInboxIntegrationEventAsync(int batchSize, short maxRetry)
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Received} OR (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND \"{nameof(ExtendedInboxIntegrationEvent.RetryCount)}\" < {maxRetry})) ORDER BY \"{nameof(ExtendedInboxIntegrationEvent.Priority)}\" DESC, \"{nameof(ExtendedInboxIntegrationEvent.Status)}\", \"{nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}\", \"{nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}\" NULLS FIRST LIMIT {batchSize}");
            }

            return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP ({batchSize}) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Received} OR ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND {nameof(ExtendedInboxIntegrationEvent.RetryCount)} < {maxRetry})) ORDER BY {nameof(ExtendedInboxIntegrationEvent.Priority)} DESC, {nameof(ExtendedInboxIntegrationEvent.Status)}, {nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}, (CASE WHEN {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)} IS NULL THEN 0 ELSE 1 END), {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}");
        }

        public async Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsRecallInboxIntegrationEventAsync(int maxMinutesAllowedProcessing)
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                var sql = $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND \"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Processing} AND extract(epoch FROM (NOW() - \"LastModificationDate\")/60)::INT >= {maxMinutesAllowedProcessing} LIMIT {BatchRecall}";
                try
                {
                    return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(sql);
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP ({BatchRecall}) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND {nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Processing} AND DATEDIFF(minute, {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}, GETUTCDATE()) >= {maxMinutesAllowedProcessing}");
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

        public async Task<int> UpdateMultiPriorityAsync(string serviceCode, string exchangeName, Guid projectInstanceId, short priority, int batchSize = 100)
        {
            var result = 0;

            try
            {
                var hasInbox = true;
                while (hasInbox)
                {
                    var listInboxEvent = await GetsUpdatePriorityAsync(serviceCode, exchangeName, projectInstanceId, priority, batchSize);
                    if (listInboxEvent != null && listInboxEvent.Any())
                    {
                        foreach (var inboxEvent in listInboxEvent)
                        {
                            inboxEvent.Priority = priority;
                            result += await UpdateAsync(inboxEvent);
                        }
                    }
                    else
                    {
                        hasInbox = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error: UpdateMultiPriorityAsync, Message: {ex.Message} ", ex);
            }

            return result;
        }
        public async Task<int> ResetRetryCountAsync(Guid intergrationEventId, short retryCount)
        {
            if (_providerName == "Npgsql")
            {
                return await _conn.ExecuteAsync($"UPDATE {_tableName} SET \"{nameof(ExtendedInboxIntegrationEvent.RetryCount)}\" = @RetryCount  WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\"=@IntergrationEventId", new
                {
                    IntergrationEventId = intergrationEventId,
                    RetryCount = retryCount
                });
            }

            return await _conn.ExecuteAsync($"DELETE {_tableName} SET {nameof(ExtendedInboxIntegrationEvent.RetryCount)} = @RetryCount WHERE {nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}=@IntergrationEventId", new
            {
                IntergrationEventId = intergrationEventId,
                RetryCount = retryCount
            });
        }

        public async Task<int> ResetMultiRetryCountsAsync(List<Guid> intergrationEventIds, short retryCount)
        {
            if (_providerName == "Npgsql")
            {
                var sql = $"UPDATE {_tableName} SET \"{nameof(ExtendedInboxIntegrationEvent.RetryCount)}\" = @RetryCount WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\" = ANY(@IntergrationEventIds)";

                return await _conn.ExecuteAsync(sql, new
                {
                    RetryCount = retryCount,
                    IntergrationEventIds = intergrationEventIds.ToArray()
                });
            }
            else
            {
                var sql = $@" UPDATE {_tableName} SET {nameof(ExtendedInboxIntegrationEvent.RetryCount)} = @RetryCount WHERE {nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} IN @IntergrationEventIds";

                return await _conn.ExecuteAsync(sql, new
                {
                    RetryCount = retryCount,
                    IntergrationEventIds = intergrationEventIds
                });
            }
        }
        #region Private methods

        private async Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsUpdatePriorityAsync(string serviceCode, string exchangeName, Guid projectInstanceId, short priority, int batchSize = 100)
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND \"{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}\" = '{serviceCode}' AND \"{nameof(ExtendedInboxIntegrationEvent.ExchangeName)}\" = '{exchangeName}' AND \"{nameof(ExtendedInboxIntegrationEvent.ProjectInstanceId)}\" = '{projectInstanceId}' AND (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Received} OR \"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Nack}) AND \"{nameof(ExtendedInboxIntegrationEvent.Priority)}\" != {priority} LIMIT {batchSize}");
            }

            return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP ({batchSize}) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND {nameof(ExtendedInboxIntegrationEvent.ServiceCode)} = '{serviceCode}' AND {nameof(ExtendedInboxIntegrationEvent.ExchangeName)} = '{exchangeName}' AND {nameof(ExtendedInboxIntegrationEvent.ProjectInstanceId)} = '{projectInstanceId}' AND ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Received} OR {nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Nack}) AND {nameof(ExtendedInboxIntegrationEvent.Priority)} != {priority}");
        }

        private string GenerateInboxIntegrationEventUpdateQuery()
        {
            var updateQuery = new StringBuilder($"UPDATE {_tableName} SET ");
            var properties = GenerateListOfProperties(GetProperties);

            properties.ForEach(property =>
            {
                if (_providerName == ProviderTypeConstants.SqlServer)
                {
                    updateQuery.Append($"{property}=@{property},");
                }
                else if (_providerName == ProviderTypeConstants.Postgre)
                {
                    updateQuery.Append($"\"{property}\"=@{property},");
                }
            });

            updateQuery.Remove(updateQuery.Length - 1, 1); //remove last comma
            if (_providerName == ProviderTypeConstants.SqlServer)
            {
                updateQuery.Append(
                    $" WHERE {nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}=@IntergrationEventId AND {nameof(ExtendedInboxIntegrationEvent.ServiceCode)}=@ServiceCode");
            }
            else if (_providerName == ProviderTypeConstants.Postgre)
            {
                updateQuery.Append(
                    $" WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\"=@IntergrationEventId AND \"{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}\"=@ServiceCode");
            }

            return updateQuery.ToString();
        }

        private string GenerateInboxIntegrationEventDeleteQuery()
        {
            var deleteQuery = new StringBuilder($"DELETE FROM {_tableName} ");
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                deleteQuery.Append($" WHERE \"{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}\"=@{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} AND \"{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}\"=@{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}");
            }
            else
            {
                deleteQuery.Append($" WHERE {nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)}=@{nameof(ExtendedInboxIntegrationEvent.IntergrationEventId)} AND {nameof(ExtendedInboxIntegrationEvent.ServiceCode)}=@{nameof(ExtendedInboxIntegrationEvent.ServiceCode)}");
            }

            return deleteQuery.ToString();
        }

        #endregion
    }
}
