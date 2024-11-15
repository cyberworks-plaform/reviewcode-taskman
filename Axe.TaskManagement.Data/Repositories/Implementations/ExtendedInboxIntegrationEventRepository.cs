using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.DapperBase.Implementations;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Enums;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
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
                    if (string.IsNullOrEmpty(entity.VirtualHost))
                    {
                        entity.VirtualHost = _virtualHost;
                    }

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
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Received} OR (\"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND {nameof(ExtendedInboxIntegrationEvent.RetryCount)} < {maxRetry})) ORDER BY \"{nameof(ExtendedInboxIntegrationEvent.Priority)}\" DESC, \"{nameof(ExtendedInboxIntegrationEvent.Status)}\", \"{nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}\", \"{nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}\" NULLS FIRST LIMIT {batchSize}");
            }

            return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP ({batchSize}) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Received} OR ({nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Nack} AND {nameof(ExtendedInboxIntegrationEvent.RetryCount)} < {maxRetry})) ORDER BY {nameof(ExtendedInboxIntegrationEvent.Priority)} DESC, {nameof(ExtendedInboxIntegrationEvent.Status)}, {nameof(ExtendedInboxIntegrationEvent.EventBusIntergrationEventCreationDate)}, (CASE WHEN {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)} IS NULL THEN 0 ELSE 1 END), {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}");
        }

        public async Task<IEnumerable<ExtendedInboxIntegrationEvent>> GetsRecallInboxIntegrationEventAsync(int maxMinutesAllowedProcessing)
        {
            if (_providerName == ProviderTypeConstants.Postgre)
            {
                return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                    $"SELECT * FROM {_tableName} WHERE \"{nameof(ExtendedInboxIntegrationEvent.VirtualHost)}\" = '{_virtualHost}' AND \"{nameof(ExtendedInboxIntegrationEvent.Status)}\" = {(short)EnumEventBus.ConsumMessageStatus.Processing} AND DATE_PART('minute', AGE(now(), \"{nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}\")) >= {maxMinutesAllowedProcessing} LIMIT {BatchRecall}");
            }

            return await _conn.QueryAsync<ExtendedInboxIntegrationEvent>(
                $"SELECT TOP ({BatchRecall}) * FROM {_tableName} WHERE {nameof(ExtendedInboxIntegrationEvent.VirtualHost)} = '{_virtualHost}' AND {nameof(ExtendedInboxIntegrationEvent.Status)} = {(short)EnumEventBus.ConsumMessageStatus.Processing} AND DATEDIFF(minute, {nameof(ExtendedInboxIntegrationEvent.LastModificationDate)}, GETUTCDATE()) >= {maxMinutesAllowedProcessing}");
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
    }
}
