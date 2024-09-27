using Axe.TaskManagement.Data.Repositories.Interfaces;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.EventBus.Lib;
using Axe.Utility.Definitions;
using Ce.Constant.Lib.Enums;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Outbox
{
    /// <summary>
    /// Outbox publisher
    /// Trong singleton service (IHostedService), không thể DI scoped service (DbContext hoặc IOutboxIntegrationEventRepository) => Inject IServiceScopeFactory vào singleton service và sử dụng nó để tạo 1 scoped service
    /// </summary>
    public class OutboxPublisher : BackgroundService
    {
        private readonly IEventBus _eventBus;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeSpan _timeSpan = TimeSpan.FromSeconds(30);

        public OutboxPublisher(IConfiguration configuration, IEventBus eventBus, IServiceScopeFactory serviceScopeFactory)
        {
            _eventBus = eventBus;
            _serviceScopeFactory = serviceScopeFactory;
            if (configuration["RabbitMq:OutboxInterval"] != null)
            {
                if (TimeSpan.TryParse(configuration["RabbitMq:OutboxInterval"], out var temp))
                {
                    _timeSpan = temp;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    using (var outboxIntegrationEventRepository = scope.ServiceProvider.GetRequiredService<IOutboxIntegrationEventRepository>())
                    {
                        var outboxEvents = await outboxIntegrationEventRepository.GetOutboxIntegrationEventV2();
                        foreach (var outboxEvent in outboxEvents)
                        {
                            if (!string.IsNullOrEmpty(outboxEvent.ExchangeName) && !string.IsNullOrEmpty(outboxEvent.Data))
                            {
                                outboxEvent.Status = (short)EnumEventBus.PublishMessageStatus.Publishing;
                                var rowAffected = await outboxIntegrationEventRepository.UpdateAsync(outboxEvent);
                                // Verify this outboxEvent record is processing
                                if (rowAffected > 0)
                                {
                                    bool isAck = false;

                                    if (outboxEvent.ExchangeName == nameof(LogJobEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<LogJobEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(DocFieldValueUpdateMultiValueEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<DocFieldValueUpdateMultiValueEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(DocUpdateFinalValueEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<DocUpdateFinalValueEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(DocFieldValueUpdateStatusCompleteEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<DocFieldValueUpdateStatusCompleteEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(DocChangeDeleteableEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<DocChangeDeleteableEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(DocFieldValueUpdateStatusWaitingEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<DocFieldValueUpdateStatusWaitingEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessSegmentLabelingEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessSegmentLabelingEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessDataEntryEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessDataEntryBoolEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryBoolEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessDataCheckEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessDataCheckEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessDataConfirmEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessDataConfirmEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessCheckFinalEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessCheckFinalEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(AfterProcessQaCheckFinalEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<AfterProcessQaCheckFinalEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == nameof(QueueLockEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<QueueLockEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == RabbitMqExchangeConstants.EXCHANGE_HEAVY_RETRY_DOC || outboxEvent.ExchangeName == nameof(RetryDocEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<RetryDocEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                    else if (outboxEvent.ExchangeName == EventBusConstants.EXCHANGE_HEAVY_JOB || outboxEvent.ExchangeName == nameof(TaskEvent).ToLower())
                                    {
                                        var evt = JsonConvert.DeserializeObject<TaskEvent>(outboxEvent.Data);
                                        isAck = _eventBus.Publish(evt, outboxEvent.ExchangeName);
                                    }
                                   
                                    else
                                    {
                                        Log.Error($"Can not publish: {outboxEvent.ExchangeName}");
                                    }

                                    if (isAck)
                                    {
                                        await outboxIntegrationEventRepository.DeleteAsync(outboxEvent);
                                    }
                                    else
                                    {
                                        outboxEvent.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                                        outboxEvent.LastModificationDate = DateTime.Now;
                                        await outboxIntegrationEventRepository.UpdateAsync(outboxEvent);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in publishing outbox event: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(_timeSpan, stoppingToken);
                }
            }
        }
    }
}
