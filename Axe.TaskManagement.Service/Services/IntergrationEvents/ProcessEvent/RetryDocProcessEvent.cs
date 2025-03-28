﻿using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.EventHanding;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Axe.Utility.MessageTemplate;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Helpers;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.EventBusRabbitMq.Lib.Interfaces;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent
{
    public interface IRetryDocProcessEvent : IBaseInboxProcessEvent<RetryDocEvent>, IDisposable { }

    public class RetryDocProcessEvent : Disposable, IRetryDocProcessEvent
    {
        private readonly IEventBus _eventBus;
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly IJobRepository _jobRepository;
        private readonly ITaskRepository _taskRepository;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IDocTypeFieldClientService _docTypeFieldClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        private bool _isParallelStep;

        private const int TimeOut = 600;   // Default HttpClient timeout is 100s

        public RetryDocProcessEvent(IEventBus eventBus,
            IBaseHttpClientFactory clientFatory,
            IJobRepository jobRepository,
            ITaskRepository taskRepository,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IDocTypeFieldClientService docTypeFieldClientService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration, IMapper mapper
            )
        {
            _eventBus = eventBus;
            _clientFatory = clientFatory;
            _jobRepository = jobRepository;
            _taskRepository = taskRepository;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _docTypeFieldClientService = docTypeFieldClientService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _mapper = mapper;
        }

        public async Task<Tuple<bool, string, string>> ProcessEvent(RetryDocEvent @event, CancellationToken ct = default)
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    Console.WriteLine("Request has been cancelled");
                    ct.ThrowIfCancellationRequested();
                    return new Tuple<bool, string, string>(false, "Request has been cancelled", null);
                }

                #region Main ProcessEvent

                try
                {
                    var accessToken = @event.AccessToken;

                    await EnrichDataJob(@event);

                    if (@event.Jobs == null || @event.Jobs.Count == 0)
                    {
                        Log.Logger.Error("list job is null or empty!");
                        return new Tuple<bool, string, string>(true, "list job is null or empty!", null);
                    }

                    var input = @event.Jobs.First().Input;
                    var inputParam = JsonConvert.DeserializeObject<InputParam>(input);
                    if (inputParam == null)
                    {
                        Log.Logger.Error("inputParam is null!");
                        return new Tuple<bool, string, string>(true, "inputParam is null!", null);
                    }

                    await EnrichData(inputParam, accessToken);

                    var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                    if (wfsInfoes == null)
                    {
                        Log.Logger.Error("wfsInfoes is null!");
                        return new Tuple<bool, string, string>(true, "wfsInfoes is null!", null);
                    }

                    var wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);
                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                    var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());

                    _isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);

                    if (!string.IsNullOrEmpty(crrWfsInfo.ServiceCode) && !string.IsNullOrEmpty(crrWfsInfo.ApiEndpoint) && !string.IsNullOrEmpty(input))
                    {
                        // ProjectStatistic: Update
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
                        ProcessingFile = 1,
                        CompleteFile = 0,
                        TotalFile = 0,
                        ProcessingDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() }
                    }
                };
                        var changeProjectStatistic = new ProjectStatisticUpdateProgressDto
                        {
                            ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                            ProjectInstanceId = inputParam.ProjectInstanceId.GetValueOrDefault(),
                            WorkflowInstanceId = inputParam.WorkflowInstanceId,
                            WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                            ActionCode = inputParam.ActionCode,
                            DocInstanceId = inputParam.DocInstanceId.GetValueOrDefault(),
                            StatisticDate = Int32.Parse(inputParam.DocCreatedDate.GetValueOrDefault().Date
                                .ToString("yyyyMMdd")),
                            ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                            ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                            ChangeUserStatistic = string.Empty,
                            TenantId = inputParam.TenantId
                        };
                        await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);

                        Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 ProcessingFile in step {inputParam.ActionCode} with DocInstanceId: {inputParam.DocInstanceId}");

                        //// 2.3. Check validate input
                        //if (!string.IsNullOrEmpty(crrWfsInfo.Input))
                        //{
                        //    var jSchema = JSchema.Parse(crrWfsInfo.Input); 
                        //    var jObject = JObject.Parse(@event.Input);
                        //    var isValid = jObject.IsValid(jSchema, out IList<string> errors);

                        //    if (isValid)
                        //    {
                        //        string serviceUri = ServiceHelper.GetServiceUriByServiceCode(crrWfsInfo.ServiceCode);
                        //        var response = await ProcessTask(@event.Input, serviceUri, crrWfsInfo.ApiEndpoint, crrWfsInfo.HttpMethodType, accessToken);
                        //        if (response.Success)
                        //        {
                        //            @event.Output = response.Data;

                        //            // 3. Trigger bước tiếp theo
                        //            if (nextWfsInfo != null && nextWfsInfo.ActionCode != ActionCodeConstants.End)
                        //            {
                        //                var evt = new TaskEvent
                        //                {
                        //                    Input = @event.Output,     // output của bước trước là input của bước sau
                        //                    AccessToken = @event.AccessToken
                        //                };
                        //                await TriggerNextStep(evt, nextWfsInfo);
                        //            }
                        //        }
                        //    }
                        //    else
                        //    {
                        //        foreach (var error in errors)
                        //        {
                        //            Log.Logger.Error("Error schema event input!");
                        //            Log.Logger.Error(error);
                        //        }
                        //    }
                        //}

                        // Ignore validate input
                        string serviceUri = ServiceHelper.GetServiceUriByServiceCode(crrWfsInfo.ServiceCode);
                        var response = await ProcessJob(input, serviceUri, crrWfsInfo.ApiEndpoint, crrWfsInfo.HttpMethodType, accessToken);

                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            var output = response.Data;

                            // Cập nhật thanh toán tiền cho worker &  hệ thống bước HIỆN TẠI (SegmentLabeling)
                            var clientInstanceId = await GetClientInstanceIdByProject(inputParam.ProjectInstanceId.GetValueOrDefault(), accessToken);
                            if (clientInstanceId != Guid.Empty)
                            {
                                var itemTransactionAdds = new List<ItemTransactionAddDto>();
                                var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                                foreach (var job in @event.Jobs)
                                {
                                    itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                                    {
                                        // Tạo 1 job thì lấy luôn Price trong inputParam & Code trong jobs.First().Code
                                        SourceUserInstanceId = clientInstanceId,
                                        ChangeAmount = job.Price,
                                        JobCode = job.Code,
                                        ProjectInstanceId = inputParam.ProjectInstanceId,
                                        WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                        WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                                        ActionCode = inputParam.ActionCode,
                                        Message =
                                            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                                        Description =
                                            string.Format(
                                                DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                                                clientInstanceId, job.Code)
                                    });
                                }

                                string jobCodes = string.Join(',',
                                    itemTransactionToSysWalletAdds.Select(x => x.JobCode));
                                var transactionAddMulti = new TransactionAddMultiDto
                                {
                                    CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, inputParam.ActionCode, jobCodes),
                                    CorrelationDescription = $"Hoàn thành các công việc {jobCodes}",
                                    ItemTransactionAdds = itemTransactionAdds,
                                    ItemTransactionToSysWalletAdds = itemTransactionToSysWalletAdds
                                };
                                await _transactionClientService.AddMultiTransactionAsync(transactionAddMulti, accessToken);
                            }

                            Log.Logger.Information($"Process{inputParam.ActionCode}: WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} with DocInstanceId: {inputParam.DocInstanceId} success!");

                            // TaskStepProgress: Update value
                            var updateTaskStepProgress = new TaskStepProgress
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
                            var updateCompleteTaskResult = await _taskRepository.UpdateProgressValue(inputParam.TaskId, updateTaskStepProgress);

                            if (updateCompleteTaskResult != null)
                            {
                                Log.Logger.Information($"TaskStepProgress: +1 CompleteJob {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} success!");
                            }
                            else
                            {
                                Log.Logger.Error($"TaskStepProgress: +1 CompleteJob {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} failure!");
                            }

                            // ProjectStatistic: Update
                            var changeProjectFileProgressAfter = new ProjectFileProgress
                            {
                                UnprocessedFile = 0,
                                ProcessingFile = 0,
                                CompleteFile = 0,
                                TotalFile = 0
                            };
                            var changeProjectStepProgressAfter = new List<ProjectStepProgress>
                    {
                        new ProjectStepProgress
                        {
                            InstanceId = crrWfsInfo.InstanceId,
                            Name = crrWfsInfo.Name,
                            ActionCode = crrWfsInfo.ActionCode,
                            ProcessingFile = -1,
                            CompleteFile = 1,
                            TotalFile = 0,
                            ProcessingDocInstanceIds = new List<Guid>
                                { inputParam.DocInstanceId.GetValueOrDefault() },
                            CompleteDocInstanceIds = new List<Guid>
                                { inputParam.DocInstanceId.GetValueOrDefault() }
                        }
                    };
                            var changeProjectStatisticAfter = new ProjectStatisticUpdateProgressDto
                            {
                                ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                ProjectInstanceId = inputParam.ProjectInstanceId.GetValueOrDefault(),
                                WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                                ActionCode = inputParam.ActionCode,
                                DocInstanceId = inputParam.DocInstanceId.GetValueOrDefault(),
                                StatisticDate = Int32.Parse(inputParam.DocCreatedDate.GetValueOrDefault().Date
                                    .ToString("yyyyMMdd")),
                                ChangeFileProgressStatistic =
                                    JsonConvert.SerializeObject(changeProjectFileProgressAfter),
                                ChangeStepProgressStatistic =
                                    JsonConvert.SerializeObject(changeProjectStepProgressAfter),
                                ChangeUserStatistic = string.Empty,
                                TenantId = inputParam.TenantId
                            };
                            await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatisticAfter, accessToken);

                            Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile in step {inputParam.ActionCode} with DocInstanceId: {inputParam.DocInstanceId}");
                            Log.Logger.Information($"Acked {nameof(TaskEvent)} step {inputParam.ActionCode}, WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} with DocInstanceId: {inputParam.DocInstanceId}");

                            // 3. Trigger bước tiếp theo
                            if (nextWfsInfoes != null && nextWfsInfoes.All(x => x.ActionCode != ActionCodeConstants.End))
                            {
                                bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                                var newInputParam = JsonConvert.DeserializeObject<InputParam>(output);
                                foreach (var nextWfsInfo in nextWfsInfoes)
                                {
                                    TaskEvent evt;
                                    if (isMultipleNextStep)
                                    {
                                        // Điều chỉnh lại Input cho evt
                                        newInputParam.ActionCode = nextWfsInfo.ActionCode;
                                        newInputParam.WorkflowStepInstanceId = nextWfsInfo.InstanceId;

                                        // Tổng hợp price cho các bước TIẾP THEO
                                        if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                        {
                                            foreach (var itemInput in newInputParam.ItemInputParams)
                                            {
                                                var itemWfsPrice = itemInput.WorkflowStepPrices.FirstOrDefault(x =>
                                                    x.InstanceId == nextWfsInfo.InstanceId);
                                                if (itemWfsPrice != null)
                                                {
                                                    itemInput.Price = itemWfsPrice.Price;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            var wfsPrice =
                                                newInputParam.WorkflowStepPrices.FirstOrDefault(x =>
                                                    x.InstanceId == nextWfsInfo.InstanceId);
                                            if (wfsPrice != null)
                                            {
                                                newInputParam.Price = wfsPrice.Price;
                                            }
                                        }

                                        evt = new TaskEvent
                                        {
                                            Input = JsonConvert.SerializeObject(newInputParam), // output của bước trước là input của bước sau
                                            AccessToken = @event.AccessToken
                                        };
                                    }
                                    else
                                    {
                                        evt = new TaskEvent
                                        {
                                            Input = output, // output của bước trước là input của bước sau
                                            AccessToken = @event.AccessToken
                                        };
                                    }

                                    bool isTriggerNextStep = false;
                                    bool isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                                    if (isNextStepRequiredAllBeforeStepComplete || _isParallelStep)
                                    {
                                        if (_isParallelStep)
                                        {
                                            var allItemInputParams =
                                                newInputParam.ItemInputParams.Select(x => x).ToList();
                                            foreach (var itemInput in allItemInputParams)
                                            {
                                                bool hasJobWaitingOrProcessingByDocFieldValueAndParallelJob =
                                                    await _jobRepository
                                                        .CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(
                                                            newInputParam.DocInstanceId.GetValueOrDefault(),
                                                            itemInput.DocFieldValueInstanceId,
                                                            itemInput.ParallelJobInstanceId);
                                                if (!hasJobWaitingOrProcessingByDocFieldValueAndParallelJob)
                                                {
                                                    var countOfExpectParallelJobs =
                                                        WorkflowHelper.CountOfExpectParallelJobs(wfsInfoes,
                                                            wfSchemaInfoes,
                                                            inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                            itemInput.DocTypeFieldInstanceId);
                                                    // Điều chỉnh lại value của ItemInputParams cho evt
                                                    var parallelJobs =
                                                        await _jobRepository
                                                            .GetJobCompleteByDocFieldValueAndParallelJob(
                                                                inputParam.DocInstanceId.GetValueOrDefault(),
                                                                itemInput.DocFieldValueInstanceId, itemInput.ParallelJobInstanceId);
                                                    if (parallelJobs.Count == countOfExpectParallelJobs)    // Số lượng parallelJobs bằng với countOfExpectParallelJobs thì mới next step
                                                    {
                                                        var oldValues = parallelJobs.Select(x => x.Value);
                                                        itemInput.Value = JsonConvert.SerializeObject(oldValues);
                                                        itemInput.IsConvergenceNextStep = true;
                                                        newInputParam.ItemInputParams = new List<ItemInputParam> { itemInput };
                                                        evt.Input = JsonConvert.SerializeObject(newInputParam);

                                                        await TriggerNextStep(evt, nextWfsInfo.ActionCode);
                                                        isTriggerNextStep = true;
                                                    }
                                                }
                                            }
                                        }
                                        else if (isNextStepRequiredAllBeforeStepComplete)
                                        {
                                            // Nếu bước TIẾP THEO yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
                                            var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), true);
                                            // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                                            var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                                            if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                                            {
                                                // Update current wfs status is error
                                                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Error, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                                if (!resultDocChangeCurrentWfsInfo.Success)
                                                {
                                                    Log.Logger.Error($"{nameof(AfterProcessCheckFinalProcessEvent)}: Error change current work flow step info for Doc!");
                                                }
                                                Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                                                return new Tuple<bool, string, string>(false,
                                                    "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                                    null);
                                            }

                                            var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                                            var hasJobWaitingOrProcessing = await _jobRepository.CheckHasJobWaitingOrProcessingByMultiWfs(inputParam.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                                            if (!hasJobWaitingOrProcessing)
                                            {
                                                await TriggerNextStep(evt, nextWfsInfo.ActionCode);
                                                isTriggerNextStep = true;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        await TriggerNextStep(evt, nextWfsInfo.ActionCode);
                                        isTriggerNextStep = true;
                                    }

                                    if (isTriggerNextStep)
                                    {
                                        // Update current wfs status is complete
                                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                        if (!resultDocChangeCurrentWfsInfo.Success)
                                        {
                                            Log.Logger.Error($"{nameof(AfterProcessCheckFinalProcessEvent)}: Error change current work flow step info for Doc!");
                                        }
                                        Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, ex.Message);
                    return new Tuple<bool, string, string>(false, ex.Message, ex.StackTrace);
                }

                #endregion

                if (ct.IsCancellationRequested)
                {
                    Console.WriteLine("Request has been cancelled");
                    ct.ThrowIfCancellationRequested();
                    return new Tuple<bool, string, string>(false, "Request has been cancelled", null);
                }

                return new Tuple<bool, string, string>(true, null, null);
            }
            
        }

        private async Task<GenericResponse<string>> ProcessJob(string input, string serviceUri, string apiEndpoint, short httpMethodType = (short)HttpClientMethodType.POST, string accessToken = null)
        {
            GenericResponse<string> response;
            var inputParam = JsonConvert.DeserializeObject<InputParam>(input);
            try
            {
                var client = _clientFatory.Create();
                if (httpMethodType == (short)HttpClientMethodType.POST)
                {
                    var model = new ModelInput { Input = input };
                    response = await client.PostAsync<GenericResponse<string>>(serviceUri, apiEndpoint, model, null, null, accessToken, timeOut: TimeOut);
                }
                else if (httpMethodType == (short)HttpClientMethodType.PUT)
                {
                    var model = new ModelInput { Input = input };
                    response = await client.PutAsync<GenericResponse<string>>(serviceUri, apiEndpoint, model, null, null, accessToken, timeOut: TimeOut);
                }
                else
                {
                    var requestParameters = new Dictionary<string, string>
                    {
                        { "input", input}
                    };
                    response = await client.GetAsync<GenericResponse<string>>(serviceUri, apiEndpoint, requestParameters, null, accessToken, timeOut: TimeOut);
                }
            }
            catch (Exception ex)
            {
                //response = GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);

                // Mark doc, task error, job error
                var resultDocChangeErrorStatus = await _docClientService.ChangeStatus(inputParam.DocInstanceId.GetValueOrDefault(), (short)EnumDoc.Status.Error, accessToken);
                if (!resultDocChangeErrorStatus.Success)
                {
                    Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: Error change doc status!");
                }

                var resultTaskChangeErrorStatus = await _taskRepository.ChangeStatus(inputParam.TaskId, (short)EnumTask.Status.Error);
                if (!resultTaskChangeErrorStatus)
                {
                    Log.Logger.Error($"{nameof(RetryDocIntegrationEventHandler)}: Error change task status!");
                }

                var errorJobs = await _jobRepository.GetJobByWfs(inputParam.DocInstanceId.GetValueOrDefault(),
                    inputParam.ActionCode, inputParam.WorkflowStepInstanceId);
                if (errorJobs.Any())
                {
                    foreach (var errorJob in errorJobs)
                    {
                        errorJob.Input = input;
                        errorJob.Status = (short)EnumJob.Status.Error;
                    }

                    await _jobRepository.UpdateMultiAsync(errorJobs);
                }

                // ProjectStatistic: Rollback
                var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                if (wfsInfoes != null)
                {
                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
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
                            CompleteFile = 0,
                            TotalFile = 0,
                            ProcessingDocInstanceIds = new List<Guid>
                                { inputParam.DocInstanceId.GetValueOrDefault() }
                        }
                    };
                    var changeProjectStatistic = new ProjectStatisticUpdateProgressDto
                    {
                        ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                        ProjectInstanceId = inputParam.ProjectInstanceId.GetValueOrDefault(),
                        WorkflowInstanceId = inputParam.WorkflowInstanceId,
                        WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                        ActionCode = inputParam.ActionCode,
                        DocInstanceId = inputParam.DocInstanceId.GetValueOrDefault(),
                        StatisticDate = Int32.Parse(inputParam.DocCreatedDate.GetValueOrDefault().Date
                            .ToString("yyyyMMdd")),
                        ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                        ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                        ChangeUserStatistic = string.Empty,
                        TenantId = inputParam.TenantId
                    };
                    await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);
                }

                throw ex;
            }

            return response;
        }

        private async Task TriggerNextStep(TaskEvent evt, string nextWfsActionCode)
        {
            bool isNextStepHeavyJob = WorkflowHelper.IsHeavyJob(nextWfsActionCode);
            // Outbox
            var exchangeName = isNextStepHeavyJob ? EventBusConstants.EXCHANGE_HEAVY_JOB : nameof(TaskEvent).ToLower();
            var outboxEntity = new OutboxIntegrationEvent
            {
                ExchangeName = exchangeName,
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(evt),
                LastModificationDate = DateTime.UtcNow,
                Status = (short)EnumEventBus.PublishMessageStatus.Nack
            };

            try // try to publish event
            {
                _eventBus.Publish(evt, exchangeName);
            }
            catch (Exception exPublishEvent)
            {
                Log.Error(exPublishEvent, $"Error publish for event {exchangeName}");

                try // try to save event to DB for retry later
                {
                    await _outboxIntegrationEventRepository.AddAsync(outboxEntity);

                }
                catch (Exception exSaveDB)
                {
                    Log.Error(exSaveDB, $"Error save DB for event {exchangeName}");
                    throw;
                }
            }

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

        #region Enrich data for InputParam

        private async Task EnrichDataJob(RetryDocEvent evt)
        {
            if (evt.Jobs == null || evt.Jobs.Count == 0)
            {
                if (evt.JobIds != null && evt.JobIds.Any())
                {
                    var jobs = await _jobRepository.GetByIdsAsync(JsonConvert.SerializeObject(evt.JobIds));
                    evt.Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs.ToList());
                }
            }
        }

        #endregion

        #region Enrich data for InputParam

        private async Task<bool> EnrichData(InputParam inputParam, string accessToken = null)
        {
            var result = false;

            // Lấy thông tin về luồng đang chạy
            List<WorkflowStepInfo> wfsInfoes = null;
            List<WorkflowSchemaConditionInfo> wfSchemaInfoes = null;
            if (string.IsNullOrEmpty(inputParam.WorkflowStepInfoes) || string.IsNullOrEmpty(inputParam.WorkflowSchemaInfoes))
            {
                var wfInfoes = await GetWfInfoes(inputParam.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                wfsInfoes = wfInfoes.Item1;
                wfSchemaInfoes = wfInfoes.Item2;
                inputParam.WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes);
                inputParam.WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes);
                result = true;
            }
            else
            {
                wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);
            }

            if (inputParam.ItemInputParams == null || inputParam.ItemInputParams.Count == 0)
            {
                var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), includeUploadStep: true);
                if (prevWfsInfoes.Any(x => x.ActionCode == ActionCodeConstants.Upload))
                {
                    var crrWfsInfo = wfsInfoes?.FirstOrDefault(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                    if (crrWfsInfo != null)
                    {
                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(crrWfsInfo.ConfigStep,
                        ConfigStepPropertyConstants.IsPaidStep);
                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                        bool isPaid = !crrWfsInfo.IsAuto || (crrWfsInfo.IsAuto && isPaidStepRs && isPaidStep);
                        decimal price = isPaid ? MoneyHelper.GetPriceByConfigPrice(crrWfsInfo.ConfigPrice, inputParam.DigitizedTemplateInstanceId) : 0;
                        var itemInputParams = new List<ItemInputParam>();

                        var docTypeFieldsRs = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(
                            inputParam.ProjectInstanceId.GetValueOrDefault(),
                            inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                        
                        if (docTypeFieldsRs != null && docTypeFieldsRs.Success && docTypeFieldsRs.Data.Any())
                        {
                            var docTypeFields = docTypeFieldsRs.Data;
                            foreach (var dtf in docTypeFields)
                            {
                                var item = new ItemInputParam
                                {
                                    FilePartInstanceId = null,
                                    DocTypeFieldId = dtf.Id,
                                    DocTypeFieldInstanceId = dtf.InstanceId,
                                    DocTypeFieldCode = dtf.Code,
                                    DocTypeFieldName = dtf.Name,
                                    DocTypeFieldSortOrder = dtf.SortOrder.GetValueOrDefault(),
                                    InputType = dtf.InputType,
                                    MaxLength = dtf.MaxLength,
                                    MinLength = dtf.MinLength,
                                    MinValue = dtf.MinValue,
                                    MaxValue = dtf.MaxValue,
                                    PrivateCategoryInstanceId = dtf.PrivateCategoryInstanceId,
                                    IsMultipleSelection = dtf.IsMultipleSelection,
                                    CoordinateArea = dtf.CoordinateArea,
                                    Value = string.Empty,
                                };
                                
                                item.Price = crrWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File ? 0 : isPaid ? price : 0;
                                itemInputParams.Add(item);
                            }
                        }

                        if (itemInputParams.Any())
                        {
                            inputParam.ItemInputParams = itemInputParams;
                            result = true;
                        }
                    }
                }
            }

            return result;
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

        #endregion
    }
}
