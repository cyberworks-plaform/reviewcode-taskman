using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.EntityExtensions;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class TaskIntegrationEventHandler : IIntegrationEventHandler<TaskEvent>
    {
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly ICommonConsumerService _commonConsumerService;

        private static string _serviceCode;

        public TaskIntegrationEventHandler(
            IConfiguration configuration,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService)
        {
            _extendedInboxIntegrationEventRepository = extendedInboxIntegrationEventRepository;
            _commonConsumerService = commonConsumerService;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }
        }

        public async Task Handle(TaskEvent @event)
        {
            if (@event != null)
            {
                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var inboxEvent = new ExtendedInboxIntegrationEvent
                {
                    IntergrationEventId = @event.IntergrationEventId,
                    EventBusIntergrationEventId = @event.EventBusIntergrationEventId,
                    EventBusIntergrationEventCreationDate = @event.EventBusIntergrationEventCreationDate,
                    EntityName = nameof(TaskEvent),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event)
                };

                // Enrich inbox event
                var inputParam = JsonConvert.DeserializeObject<InputParam>(@event.Input);
                if (inputParam != null)
                {
                    //inboxEvent.EntityId = inputParam.DocId.ToString();  // Ignore
                    inboxEvent.EntityInstanceId = inputParam.DocInstanceId;
                    inboxEvent.Path = inputParam.DocPath;
                    inboxEvent.ProjectInstanceId = inputParam.ProjectInstanceId;
                    inboxEvent.DocInstanceId = inputParam.DocInstanceId;
                }

                await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
            }
            else
            {
                Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: @event is null!");
            }
        }
    }
}
