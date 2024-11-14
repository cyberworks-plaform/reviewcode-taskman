using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Newtonsoft.Json;
using Serilog;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class AfterProcessSegmentLabelingIntegrationEventHandler : IIntegrationEventHandler<AfterProcessSegmentLabelingEvent>
    {
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IJobRepository _repository;

        private static string _serviceCode;

        public AfterProcessSegmentLabelingIntegrationEventHandler(
            IConfiguration configuration,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService,
            IJobRepository repository)
        {
            _extendedInboxIntegrationEventRepository = extendedInboxIntegrationEventRepository;
            _commonConsumerService = commonConsumerService;
            _repository = repository;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }
        }

        public async Task Handle(AfterProcessSegmentLabelingEvent @event)
        {
            if (@event != null && (@event.Job != null || !string.IsNullOrEmpty(@event.JobId)))
            {
                var jobId = @event.Job != null ? @event.Job?.Id : @event.JobId;
                Log.Logger.Information($"Start handle integration event from {nameof(AfterProcessSegmentLabelingEvent)} with JobId: {jobId}");
                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var inboxEvent = new ExtendedInboxIntegrationEvent
                {
                    IntergrationEventId = @event.IntergrationEventId,
                    EventBusIntergrationEventId = @event.EventBusIntergrationEventId,
                    EventBusIntergrationEventCreationDate = @event.EventBusIntergrationEventCreationDate,
                    EntityId = jobId,
                    EntityName = nameof(Job),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event)
                };

                // Enrich inbox event
                if (@event.Job == null)
                {
                    var crrJob = await _repository.GetByIdAsync(new ObjectId(jobId));
                    if (crrJob != null)
                    {
                        inboxEvent.EntityInstanceId = crrJob.InstanceId;
                        inboxEvent.DocInstanceId = crrJob.DocInstanceId;
                        inboxEvent.ProjectInstanceId = crrJob.ProjectInstanceId;
                        inboxEvent.Path = crrJob.DocPath;
                    }
                }

                await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
            }
            else
            {
                Log.Logger.Error($"{nameof(AfterProcessSegmentLabelingEvent)}: @event is null!");
            }
        }

        
    }
}
