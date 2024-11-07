using System;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class AfterProcessDataEntryBoolIntegrationEventHandler : IIntegrationEventHandler<AfterProcessDataEntryBoolEvent>
    {
        private readonly IAfterProcessDataEntryBoolProcessEvent _afterProcessDataEntryBoolProcessEvent;
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly IConsumerConfigClientService _consumerConfigClientService;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IJobRepository _repository;

        private static string _serviceCode;

        public AfterProcessDataEntryBoolIntegrationEventHandler(
            IConsumerConfigClientService consumerConfigClientService,
            IConfiguration configuration,
            IAfterProcessDataEntryBoolProcessEvent afterProcessDataEntryBoolProcessEvent,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService,
            IJobRepository repository)
        {
            _consumerConfigClientService = consumerConfigClientService;
            _afterProcessDataEntryBoolProcessEvent = afterProcessDataEntryBoolProcessEvent;
            _extendedInboxIntegrationEventRepository = extendedInboxIntegrationEventRepository;
            _commonConsumerService = commonConsumerService;
            _repository = repository;
            if (string.IsNullOrEmpty(_serviceCode))
            {
                _serviceCode = configuration.GetValue("ServiceCode", string.Empty);
            }
        }

        public async Task Handle(AfterProcessDataEntryBoolEvent @event)
        {
            if (@event != null && ((@event.Jobs != null && @event.Jobs.Any()) || (@event.JobIds != null && @event.JobIds.Any())))
            {
                var sw = Stopwatch.StartNew();

                string jobIds = null;
                if (@event.Jobs != null && @event.Jobs.Any())
                {
                    jobIds = string.Join(',', @event.Jobs.Select(x => x.Id));
                }
                else if (@event.JobIds != null && @event.JobIds.Any())
                {
                    jobIds = string.Join(',', @event.JobIds);
                }

                Log.Logger.Information($"Start handle integration event from {nameof(AfterProcessDataEntryBoolEvent)} with JobIds: {jobIds}");

                var exchangeName = await _commonConsumerService.GetExchangeName(GetType());
                var exchangeConfigRs = await _consumerConfigClientService.GetExchangeConfig(exchangeName, @event.AccessToken);
                var isProcessImmediate =
                    exchangeConfigRs != null && exchangeConfigRs.Success && exchangeConfigRs.Data != null
                        ? exchangeConfigRs.Data.TypeProcessing == (short)EnumEventBus.ConsumerTypeProcessing.ProcessImmediate
                        : true;
                var typeProcessing = isProcessImmediate
                    ? EnumEventBus.ConsumerTypeProcessing.ProcessImmediate
                    : EnumEventBus.ConsumerTypeProcessing.ProcessLater;
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
                    TypeProcessing = (short)typeProcessing
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

                var tryInsertInbox = await _extendedInboxIntegrationEventRepository.TryInsertInbox(inboxEvent);
                var isInsertSuccess = tryInsertInbox.Item1;
                inboxEvent = tryInsertInbox.Item2;

                if (isInsertSuccess)
                {
                    if (isProcessImmediate)
                    {
                        // Mark inbox event Processing
                        inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Processing;
                        await _extendedInboxIntegrationEventRepository.UpdateAsync(inboxEvent);

                        // Process Event
                        CancellationToken ct;
                        if (exchangeConfigRs != null && exchangeConfigRs.Success && exchangeConfigRs.Data != null && exchangeConfigRs.Data.TimeOut != default)
                        {
                            var cancellationTokenSource = new CancellationTokenSource();
                            cancellationTokenSource.CancelAfter(exchangeConfigRs.Data.TimeOut);
                            ct = cancellationTokenSource.Token;
                        }
                        else
                        {
                            ct = default;
                        }

                        try
                        {
                            var result = await _afterProcessDataEntryBoolProcessEvent.ProcessEvent(@event, ct);
                            var isAck = result.Item1;

                            // Delete inbox entity Ack or mark inbox entity Nack
                            if (isAck)
                            {
                                await _extendedInboxIntegrationEventRepository.DeleteAsync(inboxEvent);

                                sw.Stop();
                                Log.Logger.Information($"Acked {nameof(AfterProcessDataEntryBoolEvent)} with JobIds: {jobIds} - Elapsed time {sw.ElapsedMilliseconds} ms");
                            }
                            else
                            {
                                inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                                inboxEvent.Message = result.Item2;
                                inboxEvent.StackTrace = result.Item3;
                                await _extendedInboxIntegrationEventRepository.UpdateAsync(inboxEvent);

                                sw.Stop();
                                Log.Logger.Information($"Not Acked {nameof(AfterProcessDataEntryBoolEvent)} with JobIds: {jobIds} - Elapsed time {sw.ElapsedMilliseconds} ms");
                            }
                        }
                        catch (OperationCanceledException ex)
                        {
                            Log.Logger.Error(ex, ex.Message);
                            inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                            inboxEvent.Message = ex.Message;
                            inboxEvent.StackTrace = ex.StackTrace;
                            await _extendedInboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                        }
                    }
                }
            }
            else
            {
                Log.Logger.Error($"{nameof(AfterProcessDataEntryBoolEvent)}: @event is null!");
            }
        }
    }
}
