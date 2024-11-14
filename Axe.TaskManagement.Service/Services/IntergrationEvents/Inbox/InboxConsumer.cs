using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Ce.Constant.Lib.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Inbox
{
    /// <summary>
    /// Outbox publisher
    /// In your singleton service, the IHostedService, inject an IServiceScopeFactory into it and use that to create a scope and get a new DbContext from it.
    /// </summary>
    public class InboxConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeSpan _timeSpan = TimeSpan.FromSeconds(30);
        private readonly short _maxRetry = 5;

        private static bool _isRunning;
        private static int _tryDoWorkDuringRunning;

        public InboxConsumer(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            if (configuration["RabbitMq:InboxInterval"] != null)
            {
                if (TimeSpan.TryParse(configuration["RabbitMq:InboxInterval"], out var temp))
                {
                    _timeSpan = temp;
                }
            }
            if (configuration["RabbitMq:InboxMaxRetry"] != null)
            {
                if (short.TryParse(configuration["RabbitMq:InboxMaxRetry"], out var tempRetry))
                {
                    _maxRetry = tempRetry;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Reset try do work during running
                if (_tryDoWorkDuringRunning >= 3)
                {
                    _tryDoWorkDuringRunning = 0;
                }

                if (!_isRunning)
                {
                    try
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        using (var inboxIntegrationEventRepository = scope.ServiceProvider.GetRequiredService<IExtendedInboxIntegrationEventRepository>())
                        {
                            bool isAck = false;
                            string message = null;
                            string stackTrace = null;
                            short inboxEventStatus = (short)EnumEventBus.ConsumMessageStatus.Received;
                            ExtendedInboxIntegrationEvent inboxEvent = null;
                            try
                            {
                                inboxEvent = await inboxIntegrationEventRepository.GetInboxIntegrationEventAsync(_maxRetry);
                                if (inboxEvent != null)
                                {
                                    _isRunning = true;
                                    inboxEventStatus = inboxEvent.Status;

                                    if (!string.IsNullOrEmpty(inboxEvent.ExchangeName) && !string.IsNullOrEmpty(inboxEvent.Data))
                                    {
                                        // Process Event
                                        inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Processing;
                                        var rowAffected = await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                        // Verify this inboxEvent record is processing
                                        if (rowAffected > 0)
                                        {
                                            var consumerConfigClientService = scope.ServiceProvider.GetRequiredService<IConsumerConfigClientService>();

                                            // Parse & Process event
                                            if (inboxEvent.ExchangeName == nameof(AfterProcessCheckFinalEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessCheckFinalEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessCheckFinalProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessDataCheckEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessDataCheckEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataCheckProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessDataConfirmEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessDataConfirmEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataConfirmProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessDataEntryBoolEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryBoolEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataEntryBoolProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessDataEntryEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataEntryProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessQaCheckFinalEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessQaCheckFinalEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessQaCheckFinalProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(AfterProcessSegmentLabelingEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<AfterProcessSegmentLabelingEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessSegmentLabelingProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(QueueLockEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<QueueLockEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IQueueLockProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(RetryDocEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<RetryDocEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<IRetryDocProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else if (inboxEvent.ExchangeName == nameof(TaskEvent).ToLower())
                                            {
                                                var evt = JsonConvert.DeserializeObject<TaskEvent>(inboxEvent.Data);
                                                if (evt != null)
                                                {
                                                    // Check is Retry or Not
                                                    if (inboxEventStatus == (short)EnumEventBus.ConsumMessageStatus.Nack)
                                                    {
                                                        evt.IsRetry = true;
                                                    }

                                                    var exchangeConfigRs = await consumerConfigClientService.GetExchangeConfig(inboxEvent.ExchangeName, evt.AccessToken);
                                                    var ct = GetCancellationToken(exchangeConfigRs);
                                                    using var processEventService = scope.ServiceProvider.GetRequiredService<ITaskProcessEvent>();
                                                    var result = await processEventService.ProcessEvent(evt, ct);
                                                    isAck = result.Item1;
                                                    message = result.Item2;
                                                    stackTrace = result.Item3;
                                                }
                                            }
                                            else
                                            {
                                                Log.Error("Exchange name is not registered or does not exist");
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    //  Không có dữ liệu thì 30s sau mới quét
                                    await Task.Delay(_timeSpan, stoppingToken);
                                }
                            }
                            catch (OperationCanceledException ex)
                            {
                                Log.Error(ex, ex.Message);
                                message = ex.Message;
                                stackTrace = ex.StackTrace;
                                isAck = false;
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, e.StackTrace);
                                isAck = false;
                            }
                            finally
                            {
                                _isRunning = false;

                                // Reset retry count when running
                                _tryDoWorkDuringRunning = 0;

                                if (inboxEvent != null)
                                {
                                    if (isAck)
                                    {
                                        // Acked
                                        await inboxIntegrationEventRepository.DeleteAsync(inboxEvent);
                                    }
                                    else
                                    {
                                        if (inboxEventStatus == (short)EnumEventBus.ConsumMessageStatus.Nack)
                                        {
                                            inboxEvent.RetryCount++;
                                        }

                                        inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                                        inboxEvent.Message = message;
                                        inboxEvent.StackTrace = stackTrace;
                                        await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in consuming inbox event: {ex.Message}");
                    }
                }
                else
                {
                    // _isRunning == true
                    _tryDoWorkDuringRunning++;
                    if (_tryDoWorkDuringRunning >= 3)
                    {
                        await Task.Delay(_timeSpan, stoppingToken);
                    }
                }
            }
        }

        private CancellationToken GetCancellationToken(GenericResponse<ExchangeConfigDto> exchangeConfigRs)
        {
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

            return ct;
        }
    }
}
