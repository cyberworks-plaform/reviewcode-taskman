using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class QueueLockIntegrationEventHandler : IIntegrationEventHandler<QueueLockEvent>
    {
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IQueueLockRepository _queueLockRepository;

        private static string _serviceCode;

        public QueueLockIntegrationEventHandler(
            IConfiguration configuration,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService,
            IQueueLockRepository queueLockRepository)
        {
            _extendedInboxIntegrationEventRepository = extendedInboxIntegrationEventRepository;
            _commonConsumerService = commonConsumerService;
            _queueLockRepository = queueLockRepository;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }
        }

        public async Task Handle(QueueLockEvent @event)
        {
            if (@event != null && @event.ProjectInstanceId != null)
            {
                var sw = Stopwatch.StartNew();

                Log.Logger.Information(
                    $"Start handle integration event from {nameof(QueueLockIntegrationEventHandler)}: ProjectInstanceId: {@event.ProjectInstanceId} with DocPath: {@event.DocPath}");

                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var inboxEvent = new ExtendedInboxIntegrationEvent
                {
                    IntergrationEventId = @event.IntergrationEventId,
                    EventBusIntergrationEventId = @event.EventBusIntergrationEventId,
                    EventBusIntergrationEventCreationDate = @event.EventBusIntergrationEventCreationDate,
                    EntityName = nameof(QueueLock),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event),
                    ProjectInstanceId = @event.ProjectInstanceId,
                    Path = @event.DocPath
                };

                await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
            }
            else
            {
                Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: @event is null!");
            }
        }

        
    }
}
