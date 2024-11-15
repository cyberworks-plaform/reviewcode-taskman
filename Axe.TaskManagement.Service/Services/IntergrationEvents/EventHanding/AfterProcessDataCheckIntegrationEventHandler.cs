using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Newtonsoft.Json;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class AfterProcessDataCheckIntegrationEventHandler : IIntegrationEventHandler<AfterProcessDataCheckEvent>
    {
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IJobRepository _repository;

        private static string _serviceCode;

        public AfterProcessDataCheckIntegrationEventHandler(
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

        public async Task Handle(AfterProcessDataCheckEvent @event)
        {
            if (@event != null && ((@event.Jobs != null && @event.Jobs.Any()) || (@event.JobIds != null && @event.JobIds.Any())))
            {
                string jobIds = null;
                if (@event.Jobs != null && @event.Jobs.Any())
                {
                    @event.JobIds = @event.Jobs.Select(x => x.Id).ToList();
                    jobIds = string.Join(',', @event.Jobs.Select(x => x.Id));
                }
                else if (@event.JobIds != null && @event.JobIds.Any())
                {
                    jobIds = string.Join(',', @event.JobIds);
                }

                Log.Logger.Information($"Start handle integration event from {nameof(AfterProcessDataCheckEvent)} with JobIds: {jobIds}");

                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var inboxEvent = new ExtendedInboxIntegrationEvent
                {
                    IntergrationEventId = @event.IntergrationEventId,
                    EventBusIntergrationEventId = @event.EventBusIntergrationEventId,
                    EventBusIntergrationEventCreationDate = @event.EventBusIntergrationEventCreationDate,
                    EntityName = nameof(Job),
                    EntityInstanceIds = JsonConvert.SerializeObject(@event.JobIds),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event)
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
                        }
                    }
                }

                await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
            }
            else
            {
                Log.Logger.Error($"{nameof(AfterProcessDataCheckEvent)}: @event is null!");
            }
        }

    }
}
