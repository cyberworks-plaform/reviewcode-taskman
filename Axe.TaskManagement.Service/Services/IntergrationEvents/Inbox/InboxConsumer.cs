using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Inbox
{
    /// <summary>
    /// Outbox publisher
    /// In your singleton service, the IHostedService, inject an IServiceScopeFactory into it and use that to create a scope and get a new DbContext from it.
    /// </summary>
    public class InboxConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeSpan _timeSpanInboxInterval = TimeSpan.FromSeconds(10);
        private readonly short _maxRetry = 5;
        private readonly TimeSpan _inboxProcessingTimeout = TimeSpan.FromMinutes(30);
        private readonly int _batchSize = 1;
        private static bool _isRunning;
        private readonly bool _deleteAckedInbox = true;
        public InboxConsumer(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            if (configuration["RabbitMq:InboxInterval"] != null)
            {
                if (TimeSpan.TryParse(configuration["RabbitMq:InboxInterval"], out var temp))
                {
                    _timeSpanInboxInterval = temp;
                }
            }
            if (configuration["RabbitMq:InboxMaxRetry"] != null)
            {
                if (short.TryParse(configuration["RabbitMq:InboxMaxRetry"], out var tempRetry))
                {
                    _maxRetry = tempRetry;
                }
            }
            if (configuration["RabbitMq:InboxProcessingTimeout"] != null)
            {
                if (TimeSpan.TryParse(configuration["RabbitMq:InboxProcessingTimeout"], out var tempInboxProcessingTimeout))
                {
                    _inboxProcessingTimeout = tempInboxProcessingTimeout;
                }
            }
            if (configuration["RabbitMq:PrefetchCount"] != null)
            {
                if (short.TryParse(configuration["RabbitMq:PrefetchCount"], out var tempBatchSize))
                {
                    _batchSize = tempBatchSize;
                }
            }
            if (configuration["RabbitMq:DeleteAckedInbox"] != null)
            {
                if (bool.TryParse(configuration["RabbitMq:DeleteAckedInbox"], out var tempDeleteAckedInbox))
                {
                    _deleteAckedInbox = tempDeleteAckedInbox;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var isInboxEmpty = false;
                try // try consume in batch
                {
                    if (!_isRunning)
                    {
                        _isRunning = true;

                        using (var scope = _serviceScopeFactory.CreateScope())
                        {
                            using (var inboxIntegrationEventRepository = scope.ServiceProvider.GetRequiredService<IExtendedInboxIntegrationEventRepository>())
                            {
                                var currentProcessingEventList = new Dictionary<Guid, ExtendedInboxIntegrationEvent>();
                                var currentProcessingTaskList = new Dictionary<Guid, Task<ProcessEventResult>>();
                                try
                                {
                                    var listInboxEvent = await inboxIntegrationEventRepository.GetsInboxIntegrationEventAsync(_batchSize, _maxRetry);
                                    isInboxEmpty = !listInboxEvent.Any();
                                    if (isInboxEmpty)
                                    {
                                        await Task.Delay(_timeSpanInboxInterval, stoppingToken);
                                    }


                                    //step 1: mark process for event
                                    foreach (var inboxEvent in listInboxEvent)
                                    {
                                        try
                                        {
                                            if(inboxEvent.Status == (short)EnumEventBus.ConsumMessageStatus.Nack && inboxEvent.RetryCount<=0)
                                            {
                                                inboxEvent.RetryCount = 1;
                                            }    
                                            inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Processing;
                                            inboxEvent.ServiceInstanceIdProcessed = Dns.GetHostName();

                                            var rowAffected = await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                            // Verify this inboxEvent record is processing
                                            if (rowAffected > 0)
                                            {
                                                currentProcessingEventList.Add(inboxEvent.IntergrationEventId, inboxEvent);
                                                currentProcessingTaskList.Add(inboxEvent.IntergrationEventId, ProcessInboxMessage(inboxEvent));
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            //do nothing
                                        }
                                    }
                                    //release memory
                                    listInboxEvent = null;

                                    //step 2: update result of completed task then take more from DB
                                    while (currentProcessingTaskList.Any())
                                    {
                                        var listCompletedTask = currentProcessingTaskList.Where(x => x.Value.IsCompleted);

                                        // update result 
                                        foreach (var task in listCompletedTask)
                                        {
                                            try
                                            {
                                                var processEventResult = task.Value.Result;
                                                var inboxEvent = currentProcessingEventList[processEventResult.EventId];

                                                if (processEventResult.IsAck)
                                                {
                                                    // Acked
                                                    if (_deleteAckedInbox)
                                                    {
                                                        await inboxIntegrationEventRepository.DeleteAsync(inboxEvent);
                                                    }
                                                    else
                                                    {
                                                        inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Ack;
                                                        await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                                    }
                                                }
                                                else
                                                {

                                                    inboxEvent.RetryCount++;
                                                    inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                                                    inboxEvent.Message = processEventResult.Message;
                                                    inboxEvent.StackTrace = processEventResult.StackTrace;
                                                    await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Error(ex, $"Exception ocrurred when update process event result: {ex.Message}");
                                                //do nothing
                                            }
                                            finally
                                            {
                                                currentProcessingTaskList.Remove(task.Key);
                                                currentProcessingEventList.Remove(task.Key);
                                            }

                                        }

                                        //get more event from inbox 
                                        if (currentProcessingTaskList.Any() && currentProcessingTaskList.Count() < _batchSize)
                                        {
                                            var moreItem = _batchSize - currentProcessingTaskList.Count();
                                            var listMoreInboxEvent = await inboxIntegrationEventRepository.GetsInboxIntegrationEventAsync(moreItem, _maxRetry);
                                            foreach (var inboxEvent in listMoreInboxEvent)
                                            {
                                                try
                                                {
                                                    if (inboxEvent.Status == (short)EnumEventBus.ConsumMessageStatus.Nack && inboxEvent.RetryCount <= 0)
                                                    {
                                                        inboxEvent.RetryCount = 1;
                                                    }
                                                    inboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Processing;
                                                    inboxEvent.ServiceInstanceIdProcessed = Dns.GetHostName();


                                                    var rowAffected = await inboxIntegrationEventRepository.UpdateAsync(inboxEvent);
                                                    // Verify this inboxEvent record is processing
                                                    if (rowAffected > 0)
                                                    {
                                                        currentProcessingEventList.Add(inboxEvent.IntergrationEventId, inboxEvent);
                                                        currentProcessingTaskList.Add(inboxEvent.IntergrationEventId, ProcessInboxMessage(inboxEvent));
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    //do nothing ConcurrentDictionary 
                                                }
                                            }

                                            //release memory 
                                            listMoreInboxEvent = null;
                                        }
                                    }

                                }
                                catch (Exception ex)
                                {
                                    throw;
                                }
                                finally
                                {
                                    currentProcessingEventList = null;
                                    currentProcessingTaskList = null;
                                    _isRunning = false;
                                }
                            }
                        }
                    }

                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error in consuming inbox event: {ex.Message}");
                    await Task.Delay(_timeSpanInboxInterval, stoppingToken);
                    _isRunning = false;
                }
            }
        }
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            Log.Information("Inbox Consumer background service start working");
            return base.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            Log.Information("Inbox Consumer background service stop working");
            await base.StopAsync(stoppingToken);
        }

        /// <summary>
        /// Serialize event object then call logic processing function
        /// </summary>
        /// <param name="inboxEvent"></param>
        /// <returns></returns>
        private async Task<ProcessEventResult> ProcessInboxMessage(ExtendedInboxIntegrationEvent inboxEvent)
        {

            var processEventResult = new ProcessEventResult(inboxEvent.IntergrationEventId);

            short inboxEventStatus = inboxEvent.Status;
            var sw = new Stopwatch();
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                try
                {
                    sw.Restart();
                    Log.Information($"Start process inbox EventId:{inboxEvent.IntergrationEventId} - ExchangeName: {inboxEvent.ExchangeName} - DocInstanceId:{inboxEvent.DocInstanceId}");

                    var cancellationTokenSource = new CancellationTokenSource();
                    cancellationTokenSource.CancelAfter(_inboxProcessingTimeout);
                    var ct = cancellationTokenSource.Token;

                    // Parse & Process event
                    if (inboxEvent.ExchangeName == nameof(AfterProcessCheckFinalEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessCheckFinalEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessCheckFinalProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessDataCheckEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessDataCheckEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataCheckProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessDataConfirmEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessDataConfirmEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataConfirmProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessDataEntryBoolEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryBoolEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataEntryBoolProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessDataEntryEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessDataEntryEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessDataEntryProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessQaCheckFinalEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessQaCheckFinalEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessQaCheckFinalProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(AfterProcessSegmentLabelingEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<AfterProcessSegmentLabelingEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IAfterProcessSegmentLabelingProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(QueueLockEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<QueueLockEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IQueueLockProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(RetryDocEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<RetryDocEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            using var processEventService = scope.ServiceProvider.GetRequiredService<IRetryDocProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else if (inboxEvent.ExchangeName == nameof(TaskEvent).ToLower())
                    {
                        var evt = JsonConvert.DeserializeObject<TaskEvent>(inboxEvent.Data);
                        if (evt != null)
                        {
                            // Check is Retry or Not
                            if (inboxEvent.RetryCount>0)
                            {
                                evt.IsRetry = true;
                            }

                            using var processEventService = scope.ServiceProvider.GetRequiredService<ITaskProcessEvent>();
                            var result = await processEventService.ProcessEvent(evt, ct);
                            processEventResult = UpdateProcessEventResult(processEventResult, result);
                        }
                    }
                    else
                    {
                        Log.Error("Exchange name is not registered or does not exist");
                    }

                }
                catch (Exception ex)
                {
                    Log.Error($"Error in processing inbox message: {ex.Message}");
                    processEventResult.IsAck = false;
                    processEventResult.Message = ex.Message;
                    processEventResult.StackTrace = ex.StackTrace;

                }
                finally
                {
                    sw.Stop();
                    Log.Information($"End process inbox EventId:{inboxEvent.IntergrationEventId} - ExchangeName: {inboxEvent.ExchangeName} - DocInstanceId:{inboxEvent.DocInstanceId} - Elapsed time: {sw.ElapsedMilliseconds} ms");
                }
            }

            return processEventResult;
        }

        private ProcessEventResult UpdateProcessEventResult(ProcessEventResult processEventResult, Tuple<bool, string, string> taskResult)
        {
            processEventResult.IsAck = taskResult.Item1;
            processEventResult.Message = taskResult.Item2;
            processEventResult.StackTrace = taskResult.Item3;
            return processEventResult;
        }
    }
}
