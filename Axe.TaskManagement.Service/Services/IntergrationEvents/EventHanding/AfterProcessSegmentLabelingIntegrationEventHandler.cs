using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Axe.Utility.MessageTemplate;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding
{
    public class AfterProcessSegmentLabelingIntegrationEventHandler : IIntegrationEventHandler<AfterProcessSegmentLabelingEvent>
    {
        private readonly IJobRepository _repository;
        private readonly ITaskRepository _taskRepository;
        private readonly IEventBus _eventBus;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IDocFieldValueClientService _docFieldValueClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;

        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;

        public AfterProcessSegmentLabelingIntegrationEventHandler(
            IJobRepository repository,
            ITaskRepository taskRepository,
            IEventBus eventBus,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IDocFieldValueClientService docFieldValueClientService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IServiceProvider provider,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration)
        {
            _repository = repository;
            _taskRepository = taskRepository;
            _eventBus = eventBus;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _docFieldValueClientService = docFieldValueClientService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
        }

        public async Task Handle(AfterProcessSegmentLabelingEvent @event)
        {
            if (@event != null && @event.Job != null)
            {
                Log.Logger.Information($"Start handle integration event from {nameof(AfterProcessSegmentLabelingEvent)} with JobCode: {@event.Job.Code}");

                await ProcessAfterProcessSegmentLabeling(@event);

                Log.Logger.Information($"Acked {nameof(AfterProcessSegmentLabelingEvent)} with JobCode: {@event.Job.Code}");
            }
            else
            {
                Log.Logger.Error($"{nameof(AfterProcessSegmentLabelingEvent)}: @event is null!");
            }
        }

        private async Task ProcessAfterProcessSegmentLabeling(AfterProcessSegmentLabelingEvent evt)
        {
            string accessToken = evt.AccessToken;
            var job = evt.Job;
            var userInstanceId = job.UserInstanceId.GetValueOrDefault();
            var jobEnds = new List<JobDto>();

            if (string.IsNullOrEmpty(job.Value))
            {
                Log.Error("ProcessSegmentLabeling value of job is null!");
                return;
            }

            var docItems = JsonConvert.DeserializeObject<List<DocItem>>(job.Value);
            if (docItems == null || docItems.Count <= 0)
            {
                Log.Error("ProcessSegmentLabeling value of job can not be parse!");
                return;
            }

            var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
            var wfsInfoes = wfInfoes.Item1;
            var wfSchemaInfoes = wfInfoes.Item2;

            if (wfsInfoes == null || wfsInfoes.Count <= 0)
            {
                Log.Error("ProcessSegmentLabeling can not get wfsInfoes!");
                return;
            }

            var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);

            // 1. Cập nhật thanh toán tiền cho worker & hệ thống
            var clientInstanceId = await GetClientInstanceIdByProject(job.ProjectInstanceId.GetValueOrDefault(), accessToken);
            if (clientInstanceId != Guid.Empty)
            {
                var itemTransactionAdds = new List<ItemTransactionAddDto>
                {
                    new ItemTransactionAddDto
                    {
                        SourceUserInstanceId = clientInstanceId,
                        DestinationUserInstanceId = userInstanceId,
                        ChangeAmount = job.Price * (100 - job.ClientTollRatio) / 100,
                        ChangeProvisionalAmount = 0,
                        JobCode = job.Code,
                        ProjectInstanceId = job.ProjectInstanceId,
                        WorkflowInstanceId = job.WorkflowInstanceId,
                        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        ActionCode = job.ActionCode,
                        Message =
                            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        Description = string.Format(
                            DescriptionTransactionTemplate.DescriptionTranferMoneyForCompleteJob,
                            clientInstanceId, userInstanceId,
                            job.Code)
                    }
                };
                var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                if (job.ClientTollRatio > 0)
                {
                    itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                    {
                        SourceUserInstanceId = clientInstanceId,
                        ChangeAmount = job.Price * job.ClientTollRatio / 100,
                        JobCode = job.Code,
                        ProjectInstanceId = job.ProjectInstanceId,
                        WorkflowInstanceId = job.WorkflowInstanceId,
                        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        ActionCode = job.ActionCode,
                        Message =
                            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        Description =
                            string.Format(
                                DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                                clientInstanceId, job.Code)
                    });
                }
                if (job.WorkerTollRatio > 0)
                {
                    itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                    {
                        SourceUserInstanceId = userInstanceId,
                        ChangeAmount = (job.Price * (100 - job.ClientTollRatio) / 100) * job.WorkerTollRatio / 100,
                        JobCode = job.Code,
                        ProjectInstanceId = job.ProjectInstanceId,
                        WorkflowInstanceId = job.WorkflowInstanceId,
                        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        ActionCode = job.ActionCode,
                        Message =
                            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        Description = string.Format(
                            DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                            userInstanceId, job.Code)
                    });
                }
                var transactionAddMulti = new TransactionAddMultiDto
                {
                    CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Khoanh vùng & gán nhãn", job.Code),
                    CorrelationDescription = $"Hoàn thành các công việc {job.Code}",
                    ItemTransactionAdds = itemTransactionAdds,
                    ItemTransactionToSysWalletAdds = itemTransactionToSysWalletAdds
                };
                await _transactionClientService.AddMultiTransactionAsync(transactionAddMulti, accessToken);
            }
            else
            {
                Log.Logger.Error($"Can not get ClientInstanceId from ProjectInstanceId: {job.ProjectInstanceId}!");
            }

            Log.Logger.Information($"ProcessSegmentLabeling: {job.Code} with DocInstanceId: {job.DocInstanceId} success!");

            // 2. Cập nhật thống kê, report
            // 2.1. TaskStepProgress: Update value
            var updatedTaskStepProgress = new TaskStepProgress
            {
                Id = crrWfsInfo.Id,
                InstanceId = crrWfsInfo.InstanceId,
                Name = crrWfsInfo.Name,
                ActionCode = crrWfsInfo.ActionCode,
                WaitingJob = 0,
                ProcessingJob = -1,
                CompleteJob = 1,
                TotalJob = 0,
                Status = (short)EnumTaskStepProgress.Status.Complete
            };
            var taskResult = await _taskRepository.UpdateProgressValue(job.TaskId, updatedTaskStepProgress);

            if (taskResult != null)
            {
                Log.Logger.Information($"TaskStepProgress: +1 CompleteJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId}");
            }
            else
            {
                Log.Logger.Error($"TaskStepProgress: +1 CompleteJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} failure!");
            }

            // 2.2. ProjectStatistic: Update
            var changeProjectFileProgress = new ProjectFileProgress
            {
                UnprocessedFile = 0,
                ProcessingFile = 0,
                CompleteFile = 0,
                TotalFile = 0
            };
            var changeProjectStepProgress = new List<ProjectStepProgress>
            {
                new ProjectStepProgress
                {
                    InstanceId = crrWfsInfo.InstanceId,
                    Name = crrWfsInfo.Name,
                    ActionCode = crrWfsInfo.ActionCode,
                    ProcessingFile = -1,
                    CompleteFile = 1,
                    TotalFile = 0,
                    ProcessingDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() },
                    CompleteDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() }
                }
            };
            var changeProjectUser = new ProjectUser
            {
                UserWorkflowSteps = new List<UserWorkflowStep>
                {
                    new UserWorkflowStep
                    {
                        InstanceId = crrWfsInfo.InstanceId,
                        Name = crrWfsInfo.Name,
                        ActionCode = crrWfsInfo.ActionCode,
                        AmountUser = 1,
                        UserInstanceIds = new List<Guid> { userInstanceId }
                    }
                }
            };
            var changeProjectStatistic = new ProjectStatisticUpdateProgressDto
            {
                ProjectTypeInstanceId = job.ProjectTypeInstanceId,
                ProjectInstanceId = job.ProjectInstanceId.GetValueOrDefault(),
                WorkflowInstanceId = job.WorkflowInstanceId,
                WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                ActionCode = job.ActionCode,
                DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                StatisticDate = Int32.Parse(job.DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                ChangeUserStatistic = JsonConvert.SerializeObject(changeProjectUser),
                TenantId = job.TenantId
            };
            await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);

            Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile in step {job.ActionCode} with DocInstanceId: {job.DocInstanceId}");

            // 3. Cập nhật giá trị DocFieldValue & Doc
            var itemDocFieldValueUpdateValues = new List<ItemDocFieldValueUpdateValue>();
            var docTypeFieldInstanceIds = docItems.Select(x => x.DocTypeFieldInstanceId).ToList();
            var docFieldValueResult = await _docFieldValueClientService.GetByDocTypeFieldInstanceIds(
                job.DocInstanceId.GetValueOrDefault(), JsonConvert.SerializeObject(docTypeFieldInstanceIds),
                accessToken);
            if (docFieldValueResult.Success && docFieldValueResult.Data.Any())
            {
                var docFieldValues = docFieldValueResult.Data.ToList();
                foreach (var docItem in docItems)
                {
                    if (!string.IsNullOrEmpty(docItem.CoordinateArea))
                    {
                        var docFieldValue = docFieldValues.FirstOrDefault(x =>
                            x.DocTypeFieldInstanceId == docItem.DocTypeFieldInstanceId);

                        itemDocFieldValueUpdateValues.Add(new ItemDocFieldValueUpdateValue
                        {
                            InstanceId = docFieldValue?.InstanceId ?? Guid.Empty,
                            Value = docItem.Value,
                            CoordinateArea = docItem.CoordinateArea,
                            ActionCode = job.ActionCode
                        });
                    }
                }
            }

            if (itemDocFieldValueUpdateValues.Any())
            {
                var docFieldValueUpdateMultiValueEvt = new DocFieldValueUpdateMultiValueEvent
                {
                    ItemDocFieldValueUpdateValues = itemDocFieldValueUpdateValues
                };
                // Outbox
                var outboxEntity = await _outboxIntegrationEventRepository.AddAsyncV2(new OutboxIntegrationEvent
                {
                    ExchangeName = nameof(DocFieldValueUpdateMultiValueEvent).ToLower(),
                    ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                    Data = JsonConvert.SerializeObject(docFieldValueUpdateMultiValueEvt)
                });
                var isAck = _eventBus.Publish(docFieldValueUpdateMultiValueEvt, nameof(DocFieldValueUpdateMultiValueEvent).ToLower());
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

            // 4. Trigger bước tiếp theo
            var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
            if (nextWfsInfoes != null && nextWfsInfoes.Any())
            {
                if (nextWfsInfoes.All(x => x.ActionCode != ActionCodeConstants.End))
                {
                    bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                    var isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                    bool isConvergenceNextStep = isParallelStep;
                    var parallelJobInstanceId = Guid.NewGuid();

                    foreach (var nextWfsInfo in nextWfsInfoes)
                    {
                        int numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(nextWfsInfo.ConfigStep);
                        bool isDivergenceStep = isMultipleNextStep || numOfResourceInJob > 1;

                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                            ConfigStepPropertyConstants.IsPaidStep);
                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                        if (docFieldValueResult.Success && docFieldValueResult.Data.Any())
                        {
                            var docFieldValues = docFieldValueResult.Data;
                            var itemInputParams = new List<ItemInputParam>();

                            // Tổng hợp dữ liệu itemInputParams
                            foreach (var docItem in docItems)
                            {
                                var itemInput = new ItemInputParam
                                {
                                    FilePartInstanceId = null,
                                    DocTypeFieldId = docItem.DocTypeFieldId,
                                    DocTypeFieldInstanceId = docItem.DocTypeFieldInstanceId,
                                    DocTypeFieldCode = docItem.DocTypeFieldCode,
                                    DocTypeFieldName = docItem.DocTypeFieldName,
                                    DocTypeFieldSortOrder = docItem.DocTypeFieldSortOrder,
                                    InputType = docItem.InputType,
                                    MaxLength = docItem.MaxLength,
                                    MinLength = docItem.MinLength,
                                    MaxValue = docItem.MaxValue,
                                    MinValue = docItem.MinValue,
                                    PrivateCategoryInstanceId = docItem.PrivateCategoryInstanceId,
                                    IsMultipleSelection = docItem.IsMultipleSelection,
                                    CoordinateArea = docItem.CoordinateArea,
                                    Value = docItem.Value
                                };

                                if (!string.IsNullOrEmpty(docItem.CoordinateArea))
                                {
                                    var docFieldValue = docFieldValues.FirstOrDefault(x =>
                                        x.DocTypeFieldInstanceId == docItem.DocTypeFieldInstanceId);
                                    itemInput.DocFieldValueId = docFieldValue?.Id ?? 0;
                                    itemInput.DocFieldValueInstanceId = docFieldValue?.InstanceId;
                                }

                                // Tổng hợp price cho các bước TIẾP THEO
                                if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                {
                                    // Tổng hợp các thông số cho các bước TIẾP THEO
                                    itemInput.IsDivergenceStep = isDivergenceStep;
                                    itemInput.ParallelJobInstanceId = parallelJobInstanceId;
                                    itemInput.IsConvergenceNextStep = isConvergenceNextStep;

                                    // Tổng hợp price cho các bước TIẾP THEO
                                    itemInput.Price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPrice(nextWfsInfo.ConfigPrice,
                                            job.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                        : 0;
                                }

                                itemInputParams.Add(itemInput);
                            }

                            // Tổng hợp value, price cho các bước TIẾP THEO
                            decimal price = 0;
                            string value = null;
                            if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                            {
                                value = job.Value;
                                price = isPaid
                                    ? MoneyHelper.GetPriceByConfigPrice(nextWfsInfo.ConfigPrice,
                                        job.DigitizedTemplateInstanceId)
                                    : 0;
                            }

                            var output = new InputParam
                            {
                                FileInstanceId = job.FileInstanceId,
                                ActionCode = nextWfsInfo.ActionCode,
                                //ActionCodes = null,
                                DocInstanceId = job.DocInstanceId,
                                DocName = job.DocName,
                                DocCreatedDate = job.DocCreatedDate,
                                DocPath = job.DocPath,
                                TaskId = job.TaskId,
                                TaskInstanceId = job.TaskInstanceId,
                                ProjectTypeInstanceId = job.ProjectTypeInstanceId,
                                ProjectInstanceId = job.ProjectInstanceId,
                                SyncTypeInstanceId = job.SyncTypeInstanceId,
                                DigitizedTemplateInstanceId = job.DigitizedTemplateInstanceId,
                                DigitizedTemplateCode = job.DigitizedTemplateCode,
                                WorkflowInstanceId = job.WorkflowInstanceId,
                                WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                                //WorkflowStepInstanceIds = null,
                                WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),
                                WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes),
                                Value = value,
                                Price = price,
                                ClientTollRatio = job.ClientTollRatio,
                                WorkerTollRatio = job.WorkerTollRatio,
                                IsDivergenceStep =
                                    nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                    isMultipleNextStep,
                                ParallelJobInstanceId =
                                    nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                        ? parallelJobInstanceId
                                        : null,
                                IsConvergenceNextStep =
                                    nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                    isConvergenceNextStep,
                                TenantId = job.TenantId,
                                ItemInputParams = itemInputParams
                            };
                            var taskEvt = new TaskEvent
                            {
                                Input = JsonConvert.SerializeObject(output),     // output của bước trước là input của bước sau
                                AccessToken = accessToken
                            };

                            bool isTriggerNextStep = false;
                            bool isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                            if (isNextStepRequiredAllBeforeStepComplete || job.IsParallelJob)
                            {
                                if (job.IsParallelJob)
                                {
                                    bool hasJobWaitingOrProcessingByDocFieldValueAndParallelJob =
                                        await _repository
                                            .CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(
                                                job.DocInstanceId.GetValueOrDefault(),
                                                job.DocFieldValueInstanceId,
                                                job.ParallelJobInstanceId);
                                    if (!hasJobWaitingOrProcessingByDocFieldValueAndParallelJob)
                                    {
                                        var countOfExpectParallelJobs = WorkflowHelper.CountOfExpectParallelJobs(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault(), job.DocTypeFieldInstanceId);
                                        // Điều chỉnh lại value của ItemInputParams cho evt
                                        var parallelJobs =
                                            await _repository
                                                .GetJobCompleteByDocFieldValueAndParallelJob(
                                                    job.DocInstanceId.GetValueOrDefault(),
                                                    job.DocFieldValueInstanceId, job.ParallelJobInstanceId);
                                        if (parallelJobs.Count == countOfExpectParallelJobs) // Số lượng parallelJobs = countOfExpectParallelJobs thì mới next step
                                        {
                                            // Xét trường hợp tất cả parallelJobs cùng done tại 1 thời điểm
                                            bool triggerNextStepHappend =
                                                await TriggerNextStepHappened(job.DocInstanceId.GetValueOrDefault(),
                                                    job.WorkflowStepInstanceId.GetValueOrDefault(),
                                                    job.DocTypeFieldInstanceId, job.DocFieldValueInstanceId);
                                            if (!triggerNextStepHappend)
                                            {
                                                var oldValues = parallelJobs.Select(x => x.Value);
                                                output.Value = JsonConvert.SerializeObject(oldValues);
                                                output.IsConvergenceNextStep = true;
                                                taskEvt.Input = JsonConvert.SerializeObject(output);

                                                await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                                isTriggerNextStep = true;
                                            }
                                        }
                                    }
                                }
                                else if (isNextStepRequiredAllBeforeStepComplete)
                                {
                                    // Nếu bước TIẾP THEO yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
                                    var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault(), true);
                                    bool hasJobWaitingOrProcessing =
                                        await _repository.CheckHasJobWaitingOrProcessingByMultiWfs(
                                            job.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep);
                                    if (!hasJobWaitingOrProcessing)
                                    {
                                        // Xét trường hợp tất cả prevJobs cùng done tại 1 thời điểm
                                        bool triggerNextStepHappend =
                                            await TriggerNextStepHappened(job.DocInstanceId.GetValueOrDefault(),
                                                job.WorkflowStepInstanceId.GetValueOrDefault());
                                        if (!triggerNextStepHappend)
                                        {
                                            await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                            isTriggerNextStep = true;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                isTriggerNextStep = true;
                            }

                            if (isTriggerNextStep)
                            {
                                Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {job.DocInstanceId}, JobCode: {job.Code}");
                            }
                        }
                    }
                }
                else
                {
                    // đây là bước cuối cùng: nextWfsInfo.ActionCode = End
                    var nextWfsInfo = nextWfsInfoes.First();
                    jobEnds.Add(job);

                    // TaskStepProgress: Update value
                    var updateTaskStepProgress = new TaskStepProgress
                    {
                        Id = nextWfsInfo.Id,
                        InstanceId = nextWfsInfo.InstanceId,
                        Name = nextWfsInfo.Name,
                        ActionCode = nextWfsInfo.ActionCode,
                        WaitingJob = 0,
                        ProcessingJob = 0,
                        CompleteJob = 0,
                        TotalJob = 0,
                        Status = (short)EnumTaskStepProgress.Status.Complete
                    };
                    await _taskRepository.UpdateProgressValue(job.TaskId, updateTaskStepProgress, (short)EnumTask.Status.Complete);

                    // ProjectStatistic: Update
                    var changeProjectFileProgressEnd = new ProjectFileProgress
                    {
                        UnprocessedFile = 0,
                        ProcessingFile = -1,
                        CompleteFile = 1,
                        TotalFile = 0,
                        ProcessingDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() },
                        CompleteDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() }
                    };
                    var changeProjectStepProgressEnd = new List<ProjectStepProgress>();
                    var changeProjectStatisticEnd = new ProjectStatisticUpdateProgressDto
                    {
                        ProjectTypeInstanceId = job.ProjectTypeInstanceId,
                        ProjectInstanceId = job.ProjectInstanceId.GetValueOrDefault(),
                        WorkflowInstanceId = job.WorkflowInstanceId,
                        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        ActionCode = job.ActionCode,
                        DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                        StatisticDate =
                            Int32.Parse(job.DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                        ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgressEnd),
                        ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgressEnd),
                        ChangeUserStatistic = string.Empty,
                        TenantId = job.TenantId
                    };
                    await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatisticEnd, accessToken);

                    Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile for both of FileProgressStatistic with DocInstanceId: {job.DocInstanceId}");
                }
            }

            // 4.1. Sau bước HIỆN TẠI là End (ko có bước SyntheticData) thì cập nhật FinalValue cho Doc và chuyển all trạng thái DocFieldValues sang Complete
            if (jobEnds.Any())
            {
                var docInstanceIds = jobEnds.Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct().ToList();
                foreach (var docInstanceId in docInstanceIds)
                {
                    var actionCode = jobEnds.FirstOrDefault(x => x.DocInstanceId == docInstanceId)?.ActionCode;
                    var wfsIntanceId = jobEnds.FirstOrDefault(x => x.DocInstanceId == docInstanceId)?.WorkflowStepInstanceId;
                    bool hasJobWaitingOrProcessing =
                        await _repository.CheckHasJobWaitingOrProcessingByIgnoreWfs(docInstanceId, actionCode,
                            wfsIntanceId);
                    if (!hasJobWaitingOrProcessing)
                    {
                        // Update FinalValue for Doc
                        var finalValue = JsonConvert.SerializeObject(docItems);
                        var docUpdateFinalValueEvt = new DocUpdateFinalValueEvent
                        {
                            DocInstanceId = docInstanceId,
                            FinalValue = finalValue
                        };
                        // Outbox
                        var outboxEntity = await _outboxIntegrationEventRepository.AddAsyncV2(new OutboxIntegrationEvent
                        {
                            ExchangeName = nameof(DocUpdateFinalValueEvent).ToLower(),
                            ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                            Data = JsonConvert.SerializeObject(docUpdateFinalValueEvt)
                        });
                        var isAck = _eventBus.Publish(docUpdateFinalValueEvt, nameof(DocUpdateFinalValueEvent).ToLower());
                        if (isAck)
                        {
                            await _outboxIntegrationEventRepository.DeleteAsync(outboxEntity);
                        }
                        else
                        {
                            outboxEntity.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                            await _outboxIntegrationEventRepository.UpdateAsync(outboxEntity);
                        }

                        // Update all status DocFieldValues is complete
                        var docFieldValueUpdateStatusCompleteEvt = new DocFieldValueUpdateStatusCompleteEvent
                        {
                            DocFieldValueInstanceIds = docItems
                                .Select(x => x.DocFieldValueInstanceId.GetValueOrDefault()).ToList()
                        };
                        // Outbox
                        var outboxEntityDocFieldValueUpdateStatusCompleteEvent = await _outboxIntegrationEventRepository.AddAsyncV2(new OutboxIntegrationEvent
                        {
                            ExchangeName = nameof(DocFieldValueUpdateStatusCompleteEvent).ToLower(),
                            ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                            Data = JsonConvert.SerializeObject(docFieldValueUpdateStatusCompleteEvt)
                        });
                        var isAckDocFieldValueUpdateStatusCompleteEvent = _eventBus.Publish(docFieldValueUpdateStatusCompleteEvt, nameof(DocFieldValueUpdateStatusCompleteEvent).ToLower());
                        if (isAckDocFieldValueUpdateStatusCompleteEvent)
                        {
                            await _outboxIntegrationEventRepository.DeleteAsync(outboxEntityDocFieldValueUpdateStatusCompleteEvent);
                        }
                        else
                        {
                            outboxEntityDocFieldValueUpdateStatusCompleteEvent.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                            await _outboxIntegrationEventRepository.UpdateAsync(outboxEntityDocFieldValueUpdateStatusCompleteEvent);
                        }
                    }
                }
            }
        }

        private async Task TriggerNextStep(TaskEvent evt, string nextWfsActionCode)
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

        private async Task<bool> TriggerNextStepHappened(Guid docInstanceId, Guid workflowStepInstanceId, Guid? docTypeFieldInstanceId = null, Guid? docFieldValueInstanceId = null)
        {
            if (_useCache)
            {
                var strDocTypeFieldInstanceId = docTypeFieldInstanceId == null
                    ? "null"
                    : docTypeFieldInstanceId.ToString();
                var strDocFieldValueInstanceId = docFieldValueInstanceId == null
                    ? "null"
                    : docFieldValueInstanceId.ToString();

                string cacheKey = $"{docInstanceId}_{workflowStepInstanceId}_{strDocTypeFieldInstanceId}_{strDocFieldValueInstanceId}";
                var triggerNextStepHappened = await _cachingHelper.TryGetFromCacheAsync<string>(cacheKey);  // Lưu số lần trigger
                if (!string.IsNullOrEmpty(triggerNextStepHappened))
                {
                    return true;
                }
                else
                {
                    await _cachingHelper.TrySetCacheAsync(cacheKey, 1, 60);
                    return false;
                }
            }
            else
            {
                // TODO: DB
            }

            return false;
        }

        private async Task<Guid> GetClientInstanceIdByProject(Guid projectInstanceId, string accessToken = null)
        {
            var clientInstanceIdsResult =
                await _userProjectClientService.GetPrimaryUserInstanceIdByProject(projectInstanceId, accessToken);
            if (clientInstanceIdsResult != null && clientInstanceIdsResult.Success)
            {
                return clientInstanceIdsResult.Data;
            }

            return Guid.Empty;
        }

        private async Task<Tuple<List<WorkflowStepInfo>, List<WorkflowSchemaConditionInfo>>> GetWfInfoes(Guid workflowInstanceId, string accessToken = null)
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
                        ViewUrl = wfs.ViewUrl,
                        Attribute = wfs.Attribute
                    });
                }

                // Loại bỏ những bước bị ngưng xử lý
                var wfsInfoes = WorkflowHelper.GetAvailableSteps(allWfStepInfoes);

                var wfSchemaInfoes = wf.LstWorkflowSchemaConditionDto.Select(x => new WorkflowSchemaConditionInfo
                {
                    WorkflowStepFrom = x.WorkflowStepFrom,
                    WorkflowStepTo = x.WorkflowStepTo
                }).ToList();
                return new Tuple<List<WorkflowStepInfo>, List<WorkflowSchemaConditionInfo>>(wfsInfoes, wfSchemaInfoes);
            }

            return null;
        }
    }
}
