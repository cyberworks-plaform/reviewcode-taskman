using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Helpers;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Newtonsoft.Json;
using Serilog;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class QueueLockIntegrationEventHandler : IIntegrationEventHandler<QueueLockEvent>
    {
        private readonly IQueueLockRepository _queueLockRepository;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;
        private readonly IEventBus _eventBus;

        public QueueLockIntegrationEventHandler(IQueueLockRepository queueLockRepository, IEventBus eventBus,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration)
        {
            _queueLockRepository = queueLockRepository;
            _eventBus = eventBus;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
        }

        public async Task Handle(QueueLockEvent @event)
        {
            if (@event != null && @event.ProjectInstanceId != null)
            {
                var sw = Stopwatch.StartNew();

                Log.Logger.Information(
                    $"Start handle integration event from {nameof(QueueLockIntegrationEventHandler)}: ProjectInstanceId: {@event.ProjectInstanceId} with DocPath: {@event.DocPath}");

                await ProcessQueueLock(@event);

                sw.Stop();
                Log.Information($"End handle event {nameof(QueueLockIntegrationEventHandler)} Id {@event.EventBusIntergrationEventId} - Elapsed time {sw.ElapsedMilliseconds} ms ");
            }
            else
            {
                Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: @event is null!");
            }
        }

        private async Task ProcessQueueLock(QueueLockEvent evt)
        {
            var accessToken = evt.AccessToken;
            // TODO: Turning phần này, chỉ lấy 10 queue 1 lần thôi
            var filter1 = Builders<QueueLock>.Filter.Eq(x => x.ProjectInstanceId, evt.ProjectInstanceId);
            var filter2 = Builders<QueueLock>.Filter.Regex(x => x.DocPath, "^" + evt.DocPath);
            var queueLockes = await _queueLockRepository.FindAsync(filter1 & filter2);
            foreach (var queueLock in queueLockes)
            {
                var inputParam = JsonConvert.DeserializeObject<InputParam>(queueLock.InputParam);
                if (inputParam == null)
                {
                    Log.Logger.Error("inputParam is null!");
                    continue;
                }

                var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                if (wfsInfoes == null)
                {
                    Log.Logger.Error("wfsInfoes is null!");
                    continue;
                }

                var crrWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                if (crrWfsInfo != null)
                {
                    var taskEvt = new TaskEvent
                    {
                        Input = queueLock.InputParam,
                        AccessToken = accessToken
                    };
                    await TriggerTaskEvent(taskEvt, crrWfsInfo.ActionCode);
                }
            }
        }

        private async Task TriggerTaskEvent(TaskEvent evt, string nextWfsActionCode)
        {
            bool isNextStepHeavyJob = WorkflowHelper.IsHeavyJob(nextWfsActionCode);
            // Outbox
            var outboxEntity = await _outboxIntegrationEventRepository.AddAsyncV2(new OutboxIntegrationEvent
            {
                ExchangeName = isNextStepHeavyJob ? EventBusConstants.EXCHANGE_HEAVY_JOB : nameof(TaskEvent).ToLower(),
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(evt)
            });
            var isAck = _eventBus.Publish(evt, isNextStepHeavyJob ? EventBusConstants.EXCHANGE_HEAVY_JOB : nameof(TaskEvent).ToLower());
            if (isAck)
            {
                await _outboxIntegrationEventRepository.DeleteAsync(outboxEntity);
            }
            else
            {
                outboxEntity.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                await _outboxIntegrationEventRepository.UpdateAsync(outboxEntity);
            }
        }
    }
}
