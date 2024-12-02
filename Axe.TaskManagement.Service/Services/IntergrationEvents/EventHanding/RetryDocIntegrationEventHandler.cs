using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Newtonsoft.Json;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class RetryDocIntegrationEventHandler : IIntegrationEventHandler<RetryDocEvent>
    {
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly IExtendedMessagePriorityConfigClientService _extendedMessagePriorityConfigClientService;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IJobRepository _repository;

        private static string _serviceCode;

        public RetryDocIntegrationEventHandler(
            IConfiguration configuration,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            IExtendedMessagePriorityConfigClientService extendedMessagePriorityConfigClientService,
            ICommonConsumerService commonConsumerService,
            IJobRepository repository)
        {
            _extendedInboxIntegrationEventRepository = extendedInboxIntegrationEventRepository;
            _extendedMessagePriorityConfigClientService = extendedMessagePriorityConfigClientService;
            _commonConsumerService = commonConsumerService;
            _repository = repository;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }
        }

        public async Task Handle(RetryDocEvent @event)
        {
            if (@event != null && ((@event.Jobs != null && @event.Jobs.Any()) || (@event.JobIds != null && @event.JobIds.Any())))
            {
                if (@event.Jobs != null && @event.Jobs.Any())
                {
                    @event.JobIds = @event.Jobs.Select(x => x.Id).ToList();
                    Log.Logger.Information(
                        $"Start handle integration event from {nameof(RetryDocIntegrationEventHandler)}: step {@event.Jobs.First().ActionCode}, WorkflowStepInstanceId: {@event.Jobs.First().WorkflowStepInstanceId} with DocInstanceId: {@event.DocInstanceId}");
                }
                else if (@event.JobIds != null && @event.JobIds.Any())
                {
                    Log.Logger.Information(
                        $"Start handle integration event from {nameof(RetryDocIntegrationEventHandler)}: jobId {@event.JobIds.First()} with DocInstanceId: {@event.DocInstanceId}");
                }

                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var priority = (short)EnumEventBus.ConsumMessagePriority.Normal;

                var inboxEvent = new ExtendedInboxIntegrationEvent
                {
                    IntergrationEventId = @event.IntergrationEventId,
                    EventBusIntergrationEventId = @event.EventBusIntergrationEventId,
                    EventBusIntergrationEventCreationDate = @event.EventBusIntergrationEventCreationDate,
                    EntityName = nameof(Job),
                    EntityInstanceIds = JsonConvert.SerializeObject(@event.JobIds),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event),
                    DocInstanceId = @event.DocInstanceId,
                    Priority = priority
                };

                // Enrich inbox event
                if (@event.Jobs == null || !@event.Jobs.Any())
                {
                    var jobId = @event.JobIds?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(jobId))
                    {
                        var crrJob = await _repository.GetByIdAsync(new ObjectId(jobId));
                        if (crrJob != null)
                        {
                            inboxEvent.ProjectInstanceId = crrJob.ProjectInstanceId;
                            inboxEvent.Path = crrJob.DocPath;

                            var extendedMessagePriorityConfigsRs =
                                await _extendedMessagePriorityConfigClientService.GetByServiceExchangeProject(_serviceCode,
                                    exchangeName, inboxEvent.ProjectInstanceId, @event.AccessToken);
                            if (extendedMessagePriorityConfigsRs != null && extendedMessagePriorityConfigsRs.Success && extendedMessagePriorityConfigsRs.Data.Any())
                            {
                                inboxEvent.Priority = extendedMessagePriorityConfigsRs.Data.First().Priority;
                            }
                        }
                    }
                }

                await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
            }
            else
            {
                Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: @event is null!");
            }
        }

        
    }
}
