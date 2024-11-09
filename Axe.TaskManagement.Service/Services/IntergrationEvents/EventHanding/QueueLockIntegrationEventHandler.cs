using System;
using Axe.TaskManagement.Data.Repositories.Implementations;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Newtonsoft.Json;
using Serilog;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using System.Threading;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class QueueLockIntegrationEventHandler : IIntegrationEventHandler<QueueLockEvent>
    {
        private readonly IQueueLockProcessEvent _queueLockProcessEvent;
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly IConsumerConfigClientService _consumerConfigClientService;
        private readonly ICommonConsumerService _commonConsumerService;

        private readonly IQueueLockRepository _queueLockRepository;

        private static string _serviceCode;

        public QueueLockIntegrationEventHandler(
            IConsumerConfigClientService consumerConfigClientService,
            IConfiguration configuration,
            IQueueLockProcessEvent queueLockProcessEvent,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService,
            IQueueLockRepository queueLockRepository)
        {
            _consumerConfigClientService = consumerConfigClientService;
            _queueLockProcessEvent = queueLockProcessEvent;
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
                    EntityName = nameof(QueueLock),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event),
                    TypeProcessing = (short)typeProcessing,
                    ProjectInstanceId = @event.ProjectInstanceId,
                    Path = @event.DocPath
                };

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
                        if (exchangeConfigRs != null && exchangeConfigRs.Success && exchangeConfigRs.Data != null && (exchangeConfigRs.Data.TimeOut != default || exchangeConfigRs.Data.TimeOut.Ticks != 0))
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
                            var result = await _queueLockProcessEvent.ProcessEvent(@event, ct);
                            var isAck = result.Item1;

                            // Delete inbox entity Ack or mark inbox entity Nack
                            if (isAck)
                            {
                                await _extendedInboxIntegrationEventRepository.DeleteAsync(inboxEvent);

                                sw.Stop();
                                Log.Information($"Acked handle event {nameof(QueueLockIntegrationEventHandler)} Id {@event.EventBusIntergrationEventId} - Elapsed time {sw.ElapsedMilliseconds} ms ");
                            }
                            else
                            {
                                inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                                inboxEvent.Message = result.Item2;
                                inboxEvent.StackTrace = result.Item3;
                                await _extendedInboxIntegrationEventRepository.UpdateAsync(inboxEvent);

                                sw.Stop();
                                Log.Information($"Not Acked handle event {nameof(QueueLockIntegrationEventHandler)} Id {@event.EventBusIntergrationEventId} - Elapsed time {sw.ElapsedMilliseconds} ms ");
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
                Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: @event is null!");
            }
        }

        
    }
}
