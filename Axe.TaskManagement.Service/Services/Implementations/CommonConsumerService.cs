using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.EventBus.Lib.Events;
using Ce.EventBusRabbitMq.Lib.Helpers;
using Ce.EventBusRabbitMq.Lib.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class CommonConsumerService : Disposable, ICommonConsumerService
    {
        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;
        private readonly IEventBusRabbitMqAdminClient _eventBusRabbitMqAdminClient;

        private static string _serviceCode;
        private static string _env;

        public CommonConsumerService(
            IServiceProvider provider,
            IConfiguration configuration,
            IEventBusRabbitMqAdminClient eventBusRabbitMqAdminClient
            )
        {
            _eventBusRabbitMqAdminClient = eventBusRabbitMqAdminClient;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }

            if (string.IsNullOrEmpty(_env))
            {
                _env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            }
        }

        public async Task<string> GetExchangeName(Type eventHandlerType)
        {
            var queueName = RabbitMqHelper.GetCurrentQueueName(_serviceCode, _env);
            var eventType = eventHandlerType.GetInterfaces()
                .Where(type => type.IsGenericType).SelectMany(x =>
                    x.GetGenericArguments()).FirstOrDefault(type => !type.IsAbstract && !type.IsInterface &&
                                                                    type.IsAssignableTo(typeof(IntegrationEvent)));
            var eventTypeName = eventType?.Name;
            if (_useCache)
            {
                var cacheKey = $"{queueName}_{eventTypeName}";
                var cacheVal = await _cachingHelper.TryGetFromCacheAsync<string>(cacheKey);
                if (string.IsNullOrEmpty(cacheVal))
                {
                    cacheVal = await _eventBusRabbitMqAdminClient.GetExchangeByQueueRoutingKey(queueName, eventTypeName);
                    await _cachingHelper.TrySetCacheAsync(cacheKey, cacheVal, 60 * 60 * 24);  // 1 day
                }

                return cacheVal;
            }

            return await _eventBusRabbitMqAdminClient.GetExchangeByQueueRoutingKey(queueName, eventTypeName);
        }
    }
}
