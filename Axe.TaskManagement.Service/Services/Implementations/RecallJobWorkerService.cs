using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class RecallJobWorkerService : IRecallJobWorkerService
    {

        private readonly IJobRepository _repository;
        private readonly IEventBus _eventBus;
        private readonly bool _useRabbitMq;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IMapper _mapper;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;

        public RecallJobWorkerService(
            IJobRepository jobRepository,
            IWorkflowClientService workflowClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IServiceProvider provider, IDocClientService docClientService, IMapper mapper,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration)
        {
            _repository = jobRepository;
            _workflowClientService = workflowClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _docClientService = docClientService;
            _eventBus = provider.GetService<IEventBus>();
            _useRabbitMq = _eventBus != null;
            _mapper = mapper;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
        }

        /// <summary>
        /// Do RecallJobByTurn
        /// </summary>
        /// <param name="userInstanceId"></param>
        /// <param name="turnInstanceId"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task DoWork(Guid userInstanceId, Guid turnInstanceId, string accessToken)
        {
            try
            {
                await RecallJobByTurn(userInstanceId, turnInstanceId, accessToken);
            }
            catch (Exception ex)
            {
                Log.Error($"Thu hồi lỗi user {userInstanceId}, turn :{turnInstanceId}; ex :{ex.Message}; trace: {ex.StackTrace}");
                throw;
            }

        }

        /// <summary>
        /// Check first times when run service
        /// Huydq update: 2024-05-09
        /// Gọi hàm RecallJobByTurn để có thể đồng nhất logic xử lý recall và gửi event sang Distribution Job bảo để đồng bộ trạng thái DB
        /// Todo: Giải quyết mất đồng bộ trạng thái Job giứa TaskMan và Job Dis (do một số nguyên nhân)
        /// - Tạo GUI cho phép Admin có thể gọi hàm RecallAllJob chủ động trong trường hợp không nhân được event key Expired
        /// - Tạo 1 hàm khác để trigger đồng bộ các job wating phân phối từ TaskMan -> Job Dis
        /// </summary>
        /// <returns></returns>
        public async Task<string> ReCallAllJob()
        {
            string msg = string.Empty;
            try
            {
                var fitlerDueDate = Builders<Job>.Filter.Lt(x => x.DueDate, DateTime.UtcNow);
                var fitlerStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);

                var jobs = await _repository.FindAsync(fitlerStatus & fitlerDueDate);
                var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct().ToList();

                var userId_turnIDHashSet = new HashSet<string>();
                foreach (var job in jobs)
                {
                    //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                    var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                    if (!userId_turnIDHashSet.Contains(hashkey))
                    {
                        userId_turnIDHashSet.Add(hashkey);
                        await RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault());
                    }
                }
                if (jobs.Count > 0)
                {
                    msg = string.Format("Đã thực hiện thu hồi thành công cho {0} Job", jobs.Count);
                }
                else
                {
                    msg = "Không có Job nào ở trạng thái cần thu hồi";
                }
                Log.Information(msg);
            }
            catch (Exception ex)
            {
                msg = "Lỗi trong quá trình thực hiện Recall All Job: " + ex.Message;
                Log.Logger.Error(ex, msg);
            }
            return msg;
        }



        public async Task RecallJobByTurn(Guid userInstanceId, Guid turnInstanceId, string accessToken = null)
        {
            var filterUser = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
            var filterTurn = Builders<Job>.Filter.Eq(x => x.TurnInstanceId, turnInstanceId);
            var filterStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);

            var jobs = await _repository.FindAsync(filterUser & filterTurn & filterStatus);
            var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct().ToList();
            var docLockStatuses = new List<DocLockStatusDto>();
            if (accessToken != null) // nếu hàm hàm được gọi bởi RecallAllJob thì sẽ không có acessToken
            {
                docLockStatuses = (await _docClientService.CheckLockDocs(JsonConvert.SerializeObject(docInstanceIds), accessToken)).Data;
            }

            foreach (var job in jobs)
            {
                bool isLocked = false;
                if (docLockStatuses.Any())
                {
                    var crrDocLockStatus = docLockStatuses.FirstOrDefault(x => x.InstanceId == job.DocInstanceId);
                    isLocked = crrDocLockStatus != null ? crrDocLockStatus.IsLocked : false;
                }
                job.TurnInstanceId = null;
                if (job.ActionCode == nameof(ActionCodeConstants.DataEntry))
                {
                    job.IsIgnore = false;
                    job.ReasonIgnore = null;
                    job.IsWarning = false;
                    job.ReasonWarning = null;
                }
                if (job.ActionCode == nameof(ActionCodeConstants.DataCheck))
                {
                    job.IsIgnore = false;
                    job.ReasonIgnore = null;
                }

                job.Status = isLocked ? (short)EnumJob.Status.Locked : (short)EnumJob.Status.Waiting;
                job.UserInstanceId = null;
                if (job.StartWaitingDate.HasValue)
                {
                    job.LastModificationDate = job.StartWaitingDate;
                }
                job.OldValue = RemoveUnwantedJobOldValue(job.OldValue);
            }

            var resultUpdate = 0;

            if (jobs.Count > 0 && docInstanceIds.Count > 0)
            {
                resultUpdate = await _repository.UpdateMultiAsync(jobs);
                //await UnLockDeleteDoc(docInstanceIds);
                //await UpdateDocFieldValueStatus(jobs.Where(x => x.DocFieldValueInstanceId.HasValue)
                //    .Select(x => x.DocFieldValueInstanceId).Distinct().ToList());
            }

            // Update ProjectStatistic
            if (resultUpdate > 0 && jobs.Any() && docInstanceIds.Any())
            {
                var crrJob = jobs.First();
                var crrWfsInfoes = await GetAvailableWfsInfoes(crrJob.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                var crrWfsInfoUpdate = crrWfsInfoes.First(x => x.InstanceId == crrJob.WorkflowStepInstanceId);
                // Update current wfs status is waiting
                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(docInstanceIds), crrWfsInfoUpdate.Id, (short)EnumJob.Status.Waiting, accessToken: accessToken);
                if (!resultDocChangeCurrentWfsInfo.Success)
                {
                    Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceIds: {JsonConvert.SerializeObject(docInstanceIds)} to waiting !");
                }
                // Publish message sang DistributionJob
                var publishJobs = jobs.Where(c => c.Status == (short)EnumJob.Status.Waiting).ToList();
                if (publishJobs.Count > 0)
                {
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(publishJobs),
                        AccessToken = accessToken
                    };
                    // Outbox
                    var outboxEntity = new OutboxIntegrationEvent
                    {
                        ExchangeName = nameof(LogJobEvent).ToLower(),
                        ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                        Data = JsonConvert.SerializeObject(logJobEvt),
                        LastModificationDate = DateTime.UtcNow,
                        Status = (short)EnumEventBus.PublishMessageStatus.Nack
                    };
                    try
                    {
                        _eventBus.Publish(logJobEvt, nameof(LogJobEvent).ToLower());
                    }
                    catch (Exception exPublishEvent)
                    {
                        Log.Error(exPublishEvent, "Error publish for event LogJobEvent");
                        try
                        {
                            await _outboxIntegrationEventRepository.AddAsync(outboxEntity);
                        }
                        catch (Exception exSaveDB)
                        {
                            Log.Error(exSaveDB, "Error save DB for event LogJobEvent");
                            //do nothing 
                        }
                    }
                }

                //acessToken = null khi hàm này được gọi từ RecallAllJob
                if (!string.IsNullOrEmpty(accessToken))
                {
                    var job = jobs.First();
                    var projectTypeInstanceId = job.ProjectTypeInstanceId;
                    var projectInstanceId = job.ProjectInstanceId.GetValueOrDefault();
                    var actionCode = job.ActionCode;
                    var wfsInfoes = await GetAvailableWfsInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);

                    foreach (var docInstanceId in docInstanceIds)
                    {
                        var crrJobs = jobs.Where(x => x.DocInstanceId == docInstanceId).ToList();

                        // Nếu tồn tại job Complete thì ko chuyển trạng thái về Processing
                        var hasJobComplete =
                            await _repository.CheckHasJobCompleteByWfs(docInstanceId, actionCode, crrWfsInfo.InstanceId);
                        if (!hasJobComplete)
                        {
                            var changeProjectFileProgress = new ProjectFileProgress
                            {
                                UnprocessedFile = 0,
                                ProcessingFile = 0,
                                CompleteFile = 0,
                                TotalFile = 0
                            };
                            var changeProjectStepProgress = crrJobs
                                .GroupBy(x => new
                                { x.ProjectInstanceId, x.WorkflowInstanceId, x.WorkflowStepInstanceId, x.ActionCode })
                                .Select(grp => new ProjectStepProgress
                                {
                                    InstanceId = grp.Key.WorkflowStepInstanceId.GetValueOrDefault(),
                                    Name = string.Empty,
                                    ActionCode = grp.Key.ActionCode,
                                    ProcessingFile = -grp.Select(i => i.DocInstanceId.GetValueOrDefault()).Distinct()
                                                         .Count(),
                                    CompleteFile = 0,
                                    TotalFile = 0,
                                    ProcessingDocInstanceIds = new List<Guid> { docInstanceId }
                                }).ToList();
                            var changeProjectStatistic = new ProjectStatisticUpdateProgressDto
                            {
                                ProjectTypeInstanceId = projectTypeInstanceId,
                                ProjectInstanceId = projectInstanceId,
                                WorkflowInstanceId = job.WorkflowInstanceId,
                                WorkflowStepInstanceId = crrWfsInfo.InstanceId,
                                ActionCode = job.ActionCode,
                                DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                                StatisticDate = Int32.Parse(crrJobs.First().DocCreatedDate.GetValueOrDefault().Date
                                .ToString("yyyyMMdd")),
                                ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                                ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                                ChangeUserStatistic = string.Empty,
                                TenantId = crrJobs.First().TenantId
                            };
                            await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);

                            int increaseProcessingFile = changeProjectStepProgress.Sum(s => s.ProcessingFile);
                            Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: -{increaseProcessingFile} ProcessingFile for StepProgressStatistic with DocInstanceId: {docInstanceId}");
                        }
                    }
                }
            }
        }

        #region Private
        private async Task UnLockDeleteDoc(List<Guid> lstDocId)
        {
            if (lstDocId != null && lstDocId.Count > 0)
            {
                var castList = lstDocId.ConvertAll<Guid?>(i => i).ToList();
                var fitler = Builders<Job>.Filter.In(x => x.DocInstanceId, castList);
                var lstJob = await _repository.FindAsync(fitler);
                var lstDocCantDelete = lstJob.Where(x => x.UserInstanceId != null).Select(x => x.DocInstanceId).ToList();
                var lstDocUnlock = lstJob.Where(x => !lstDocCantDelete.Contains(x.DocInstanceId)).Where
                    (x => x.DocInstanceId.HasValue).Select(x => x.DocInstanceId.Value).Distinct().ToList();
                if (lstDocUnlock != null && lstDocUnlock.Count > 0)
                {
                    // Publish DocEvent to EventBus
                    var evt = new DocChangeDeleteableEvent
                    {
                        DocInstanceIds = lstDocUnlock
                    };
                    if (_useRabbitMq)
                    {
                        // Outbox
                        await PublishEvent<DocChangeDeleteableEvent>(evt);
                    }
                }
            }
        }

        /// <summary>
        /// This function extreamly slow -> if using need to refactor
        /// </summary>
        /// <param name="lstDocFieldValue"></param>
        /// <returns></returns>
        private async Task UpdateDocFieldValueStatus(List<Guid?> lstDocFieldValue)
        {
            if (lstDocFieldValue != null && lstDocFieldValue.Count > 0)
            {
                var lstDocFieldValueUpdate = new List<Guid>();
                foreach (var item in lstDocFieldValue)
                {
                    var fitler = Builders<Job>.Filter.Eq(x => x.DocFieldValueInstanceId, item);

                    var filterWaiting = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);

                    var countOfAll = await _repository.CountAsync(fitler);
                    var countOfWaiting = await _repository.CountAsync(fitler & filterWaiting);
                    if (countOfAll == countOfWaiting)
                    {
                        lstDocFieldValueUpdate.Add(item.Value);
                    }
                }

                if (lstDocFieldValueUpdate.Count > 0)
                {
                    var evt = new DocFieldValueUpdateStatusWaitingEvent
                    {
                        DocFieldValueInstanceIds = lstDocFieldValueUpdate
                    };
                    if (_useRabbitMq)
                    {
                        // Outbox
                        await PublishEvent<DocFieldValueUpdateStatusWaitingEvent>(evt);
                    }
                }
            }
        }

        private async Task<List<WorkflowStepInfo>> GetAvailableWfsInfoes(Guid workflowInstanceId, string accessToken = null)
        {
            var wfResult = await _workflowClientService.GetByInstanceIdAsync(workflowInstanceId, accessToken);
            if (wfResult.Success && wfResult.Data != null)
            {
                var wf = wfResult.Data;

                var allWfStepInfoes = new List<WorkflowStepInfo>();
                foreach (var wfs in wf.LstWorkflowStepDto)
                {
                    allWfStepInfoes.Add(new WorkflowStepInfo
                    {
                        Id = wfs.Id,
                        InstanceId = wfs.InstanceId,
                        Name = wfs.Name,
                        ActionCode = wfs.ActionCode,
                        ConfigPrice = wfs.ConfigPrice,
                        ConfigStep = wfs.ConfigStep,
                        ServiceCode = wfs.ServiceCode,
                        ApiEndpoint = wfs.ApiEndpoint,
                        HttpMethodType = wfs.HttpMethodType,
                        IsAuto = wfs.IsAuto,
                        ViewUrl = wfs.ViewUrl
                    });
                }

                // Loại bỏ những bước bị ngưng xử lý
                return WorkflowHelper.GetAvailableSteps(allWfStepInfoes);
            }

            return null;
        }

        private async Task PublishEvent<T>(object eventData)
        {
            // Outbox LogJob
            var outboxEvent = new OutboxIntegrationEvent
            {
                ExchangeName = typeof(T).Name.ToLower(),
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(eventData),
                LastModificationDate = DateTime.UtcNow,
                Status = (short)EnumEventBus.PublishMessageStatus.Nack
            };
            try
            {
                _eventBus.Publish(eventData, outboxEvent.ExchangeName);
            }
            catch (Exception exPublishEvent)
            {
                Log.Error(exPublishEvent, $"Error publish for event {outboxEvent.ExchangeName}");

                //save to DB for retry later
                try
                {
                    await _outboxIntegrationEventRepository.AddAsync(outboxEvent);
                }
                catch (Exception exSaveDB)
                {
                    Log.Error(exSaveDB, $"Error save DB for event {outboxEvent.ExchangeName}");
                    throw;
                }
            }
        }

        /// <summary>
        /// convert DocItem => StoreDocItem to reduce size of mongoDB
        /// </summary>
        /// <param name="jobOldValue"></param>
        /// <returns></returns>
        private string RemoveUnwantedJobOldValue(string jobOldValue)
        {
            var result = jobOldValue;  
            try
            {
                if (!string.IsNullOrEmpty(jobOldValue))
                {
                    var docItems = JsonConvert.DeserializeObject<List<DocItem>>(jobOldValue);
                    if (docItems != null && docItems.Any())
                    {
                        var storedDocItems = _mapper.Map<List<DocItem>, List<StoredDocItem>>(docItems);
                        result = JsonConvert.SerializeObject(storedDocItems);
                    }
                }
            }
            catch (Exception ex)
            {
                //Do nothing
            }
            return result;
        }

        #endregion
    }
}
