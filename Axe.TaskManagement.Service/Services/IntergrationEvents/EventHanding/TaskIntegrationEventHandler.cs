using System;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Axe.Utility.EntityExtensions;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class TaskIntegrationEventHandler : IIntegrationEventHandler<TaskEvent>
    {
        private readonly ITaskProcessEvent _taskProcessEvent;
        private readonly IExtendedInboxIntegrationEventRepository _extendedInboxIntegrationEventRepository;
        private readonly IConsumerConfigClientService _consumerConfigClientService;
        private readonly ICommonConsumerService _commonConsumerService;

        private static string _serviceCode;

        public TaskIntegrationEventHandler(
            IConsumerConfigClientService consumerConfigClientService,
            IConfiguration configuration,
            ITaskProcessEvent taskProcessEvent,
            IExtendedInboxIntegrationEventRepository extendedInboxIntegrationEventRepository,
            ICommonConsumerService commonConsumerService)
        {
            _consumerConfigClientService = consumerConfigClientService;
            _taskProcessEvent = taskProcessEvent;
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
                    EntityName = nameof(TaskEvent),
                    ExchangeName = exchangeName,
                    ServiceCode = _serviceCode,
                    Data = JsonConvert.SerializeObject(@event),
                    TypeProcessing = (short)typeProcessing
                };

                // Enrich inbox event
                var inputParam = JsonConvert.DeserializeObject<InputParam>(@event.Input);
                if (inputParam != null)
                {
                    //inboxEvent.EntityId = inputParam.DocId.ToString();  // Ignore
                    inboxEvent.EntityInstanceId = inputParam.DocInstanceId;
                    inboxEvent.Path = inputParam.DocPath;
                    inboxEvent.EntityInstanceId = inputParam.DocInstanceId;
                    inboxEvent.DocInstanceId = inputParam.DocInstanceId;
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
                            var result = await _taskProcessEvent.ProcessEvent(@event, ct);
                            var isAck = result.Item1;

                            // Delete inbox entity Ack or mark inbox entity Nack
                            if (isAck)
                            {
                                await _extendedInboxIntegrationEventRepository.DeleteAsync(inboxEvent);
                            }
                            else
                            {
                                inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                                inboxEvent.Message = result.Item2;
                                inboxEvent.StackTrace = result.Item3;
                                await _extendedInboxIntegrationEventRepository.UpdateAsync(inboxEvent);
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
                Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: @event is null!");
            }
        }
    }
}
