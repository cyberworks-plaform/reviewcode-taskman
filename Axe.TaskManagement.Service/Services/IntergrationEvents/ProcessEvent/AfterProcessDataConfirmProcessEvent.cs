﻿using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Implementations;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Axe.Utility.MessageTemplate;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.EventBusRabbitMq.Lib.Interfaces;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent
{
    public interface IAfterProcessDataConfirmProcessEvent : IBaseInboxProcessEvent<AfterProcessDataConfirmEvent>,
        IDisposable
    {
    }

    public class AfterProcessDataConfirmProcessEvent : Disposable, IAfterProcessDataConfirmProcessEvent
    {
        private readonly IJobRepository _repository;
        private readonly ITaskRepository _taskRepository;
        private readonly IEventBus _eventBus;
        private readonly IMapper _mapper;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;

        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;
        private readonly IDocTypeFieldClientService _docTypeFieldClientService;

        public AfterProcessDataConfirmProcessEvent(
            IJobRepository repository,
            ITaskRepository taskRepository,
            IEventBus eventBus,
            IMapper mapper,
            IWorkflowClientService workflowClientService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IDocClientService docClientService,
            IServiceProvider provider,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration,
            IDocTypeFieldClientService docTypeFieldClientService)
        {
            _repository = repository;
            _taskRepository = taskRepository;
            _eventBus = eventBus;
            _mapper = mapper;
            _workflowClientService = workflowClientService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _docClientService = docClientService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
            _docTypeFieldClientService = docTypeFieldClientService;
        }

        public async Task<Tuple<bool, string, string>> ProcessEvent(AfterProcessDataConfirmEvent evt, CancellationToken ct = default)
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
                    string accessToken = evt.AccessToken;

                    await EnrichDataJob(evt);

                    var jobs = evt.Jobs;
                    var userInstanceId = jobs.First().UserInstanceId.GetValueOrDefault();
                    var jobEnds = new List<JobDto>();
                    var itemDocFieldValueUpdateValues = new List<ItemDocFieldValueUpdateValue>();
                    var projectInstanceId = jobs.FirstOrDefault()?.ProjectInstanceId;
                    var clientInstanceId =
                        await GetClientInstanceIdByProject(projectInstanceId.GetValueOrDefault(), accessToken);
                    var itemTransactionAdds = new List<ItemTransactionAddDto>();
                    var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                    var completeJobCodes = new List<string>();
                    var lstDocInstanceIds = jobs.Select(x => x.DocInstanceId).Distinct().ToList();
                    

                    var lstJobEntryCheckWrong = new List<Job>();
                    var lstJobEntryCheckRight = new List<Job>();
                    var dicDocItemByTemplate = new Dictionary<Guid, List<DocItem>>();
                    foreach (var job in jobs)
                    {
                        var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                        var wfsInfoes = wfInfoes.Item1;
                        var wfSchemaInfoes = wfInfoes.Item2;

                        if (wfsInfoes == null || wfsInfoes.Count <= 0)
                        {
                            throw new Exception("AfterProcessDataConfirmEvent can not get wfsInfoes!");
                        }

                        var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);
                        var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes,
                            job.WorkflowStepInstanceId.GetValueOrDefault());

                        // 1. Cập nhật thanh toán tiền cho worker & hệ thống
                        if (clientInstanceId != Guid.Empty)
                        {
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
                                    Message = string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name,
                                        job.Code),
                                    Description =
                                        string.Format(
                                            DescriptionTransactionTemplate
                                                .DescriptionTranferMoneyToSysWalletForCompleteJob,
                                            clientInstanceId, job.Code)
                                });
                            }

                            if (job.WorkerTollRatio > 0)
                            {
                                itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                                {
                                    SourceUserInstanceId = userInstanceId,
                                    ChangeAmount = (job.Price * (100 - job.ClientTollRatio) / 100) * job.WorkerTollRatio /
                                                   100,
                                    JobCode = job.Code,
                                    ProjectInstanceId = job.ProjectInstanceId,
                                    WorkflowInstanceId = job.WorkflowInstanceId,
                                    WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                                    ActionCode = job.ActionCode,
                                    Message = string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name,
                                        job.Code),
                                    Description =
                                        string.Format(
                                            DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                                            userInstanceId, job.Code)
                                });
                            }

                            var itemTransactionAdd = new ItemTransactionAddDto
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
                                Message = string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                                Description =
                                    string.Format(DescriptionTransactionTemplate.DescriptionTranferMoneyForCompleteJob,
                                        clientInstanceId, userInstanceId, job.Code)
                            };
                            itemTransactionAdds.Add(itemTransactionAdd);

                            completeJobCodes.Add(job.Code);

                            // Nếu bước TRƯỚC nếu là DataEntry thì cũng tạo Transaction thanh toán tiền cho worker
                            var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                                job.WorkflowStepInstanceId.GetValueOrDefault());
                            if (prevWfsInfoes != null && prevWfsInfoes.Any() &&
                                prevWfsInfoes.FirstOrDefault(x => x.ActionCode == ActionCodeConstants.DataEntry) != null)
                            {
                                var prevWfsInfo = prevWfsInfoes.First(x => x.ActionCode == ActionCodeConstants.DataEntry);
                                var crrJob = _mapper.Map<JobDto, Job>(job);
                                var prevDataEntryJobs =
                                    await _repository.GetPrevJobs(crrJob, new List<Guid> { prevWfsInfo.InstanceId });
                                foreach (var prevDataEntryJob in prevDataEntryJobs)
                                {
                                    if (!prevDataEntryJob.IsIgnore)
                                    {
                                        if (job.Value == prevDataEntryJob.Value)
                                        {
                                            prevDataEntryJob.RightStatus = (short)EnumJob.RightStatus.Correct;
                                            lstJobEntryCheckRight.Add((Job)prevDataEntryJob);

                                            if (prevDataEntryJob.ClientTollRatio > 0)
                                            {
                                                itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                                                {
                                                    SourceUserInstanceId = clientInstanceId,
                                                    ChangeAmount =
                                                        prevDataEntryJob.Price * prevDataEntryJob.ClientTollRatio / 100,
                                                    JobCode = prevDataEntryJob.Code,
                                                    ProjectInstanceId = prevDataEntryJob.ProjectInstanceId,
                                                    WorkflowInstanceId = prevDataEntryJob.WorkflowInstanceId,
                                                    WorkflowStepInstanceId = prevDataEntryJob.WorkflowStepInstanceId,
                                                    ActionCode = prevDataEntryJob.ActionCode,
                                                    Message = string.Format(MsgTransactionTemplate.MsgJobInfoes,
                                                        prevWfsInfo?.Name,
                                                        prevDataEntryJob.Code),
                                                    Description =
                                                        string.Format(
                                                            DescriptionTransactionTemplate
                                                                .DescriptionTranferMoneyToSysWalletForCompleteJob,
                                                            clientInstanceId, prevDataEntryJob.Code)
                                                });
                                            }

                                            if (prevDataEntryJob.WorkerTollRatio > 0)
                                            {
                                                itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                                                {
                                                    SourceUserInstanceId =
                                                        prevDataEntryJob.UserInstanceId.GetValueOrDefault(),
                                                    ChangeAmount =
                                                        (prevDataEntryJob.Price * (100 - prevDataEntryJob.ClientTollRatio) /
                                                         100) * prevDataEntryJob.WorkerTollRatio / 100,
                                                    JobCode = prevDataEntryJob.Code,
                                                    ProjectInstanceId = prevDataEntryJob.ProjectInstanceId,
                                                    WorkflowInstanceId = prevDataEntryJob.WorkflowInstanceId,
                                                    WorkflowStepInstanceId = prevDataEntryJob.WorkflowStepInstanceId,
                                                    ActionCode = prevDataEntryJob.ActionCode,
                                                    Message = string.Format(MsgTransactionTemplate.MsgJobInfoes,
                                                        prevWfsInfo?.Name,
                                                        prevDataEntryJob.Code),
                                                    Description =
                                                        string.Format(
                                                            DescriptionTransactionTemplate
                                                                .DescriptionTranferMoneyToSysWalletForCompleteJob,
                                                            userInstanceId,
                                                            prevDataEntryJob.Code)
                                                });
                                            }

                                            var correctDataEntryItemTransactionAdd = new ItemTransactionAddDto
                                            {
                                                SourceUserInstanceId = clientInstanceId,
                                                DestinationUserInstanceId =
                                                    prevDataEntryJob.UserInstanceId.GetValueOrDefault(),
                                                ChangeAmount = prevDataEntryJob.Price *
                                                    (100 - prevDataEntryJob.ClientTollRatio) / 100,
                                                ChangeProvisionalAmount = -(prevDataEntryJob.Price *
                                                    (100 - prevDataEntryJob.ClientTollRatio) / 100),
                                                JobCode = prevDataEntryJob.Code,
                                                ProjectInstanceId = prevDataEntryJob.ProjectInstanceId,
                                                WorkflowInstanceId = prevDataEntryJob.WorkflowInstanceId,
                                                WorkflowStepInstanceId = prevDataEntryJob.WorkflowStepInstanceId,
                                                ActionCode = prevDataEntryJob.ActionCode,
                                                Message = string.Format(MsgTransactionTemplate.MsgJobInfoes,
                                                    prevWfsInfo?.Name, prevDataEntryJob.Code),
                                                Description = string.Format(
                                                    DescriptionTransactionTemplate
                                                        .DescriptionTranferMoneyForConfirmedDataEntryJob,
                                                    clientInstanceId, prevDataEntryJob.UserInstanceId.GetValueOrDefault(),
                                                    job.Code)
                                            };
                                            itemTransactionAdds.Add(correctDataEntryItemTransactionAdd);
                                        }
                                        else
                                        {
                                            prevDataEntryJob.RightStatus = (short)EnumJob.RightStatus.Wrong;
                                            lstJobEntryCheckWrong.Add((Job)prevDataEntryJob);

                                            // Trường hợp worker Nhập liệu sai, worker & hệ thống sẽ ko được nhận tiền thực
                                            var wrongDataEntryItemTransactionAdd = new ItemTransactionAddDto
                                            {
                                                DestinationUserInstanceId =
                                                    prevDataEntryJob.UserInstanceId.GetValueOrDefault(),
                                                ChangeAmount = 0,
                                                ChangeProvisionalAmount = -(prevDataEntryJob.Price *
                                                    (100 - prevDataEntryJob.ClientTollRatio) / 100),
                                                JobCode = prevDataEntryJob.Code,
                                                ProjectInstanceId = prevDataEntryJob.ProjectInstanceId,
                                                WorkflowInstanceId = prevDataEntryJob.WorkflowInstanceId,
                                                WorkflowStepInstanceId = prevDataEntryJob.WorkflowStepInstanceId,
                                                ActionCode = prevDataEntryJob.ActionCode,
                                                Message = string.Format(MsgTransactionTemplate.MsgJobInfoes,
                                                    prevWfsInfo?.Name, prevDataEntryJob.Code),
                                                Description = string.Format(
                                                    DescriptionTransactionTemplate
                                                        .DescriptionDecreaseProvisionalMoneyForConfirmDataEntryJobIsWrong,
                                                    prevDataEntryJob.UserInstanceId.GetValueOrDefault(),
                                                    job.Code)
                                            };
                                            itemTransactionAdds.Add(wrongDataEntryItemTransactionAdd);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Log.Logger.Error(
                                $"Can not get ClientInstanceId from ProjectInstanceId: {job.ProjectInstanceId}!");
                        }

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
                            Log.Logger.Information(
                                $"TaskStepProgress: +1 CompleteJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId}");
                        }
                        else
                        {
                            Log.Logger.Error(
                                $"TaskStepProgress: +1 CompleteJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} failure!");
                        }

                        // 2.2. ProjectStatistic: Update
                        var changeProjectFileProgress = new ProjectFileProgress();
                        var changeProjectStepProgress = new List<ProjectStepProgress>();
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
                                UserInstanceIds = new List<Guid> {userInstanceId}
                            }
                        }
                        };
                        // Nếu ko tồn tại job Waiting hoặc Processing ở các bước TRƯỚC và bước HIỆN TẠI thì mới chuyển trạng thái
                        var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes,
                            job.WorkflowStepInstanceId.GetValueOrDefault(), true);
                        // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                        var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(job.ProjectInstanceId.GetValueOrDefault(), job.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                        if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                        {
                            Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                            return new Tuple<bool, string, string>(false,
                                "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                null);
                        }

                        var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                        var hasJobWaitingOrProcessing = await _repository.CheckHasJobWaitingOrProcessingByMultiWfs(job.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                        if (!hasJobWaitingOrProcessing)
                        {
                            Log.Information(
                                $"ProcessDataConfirm change step: DocInstanceId => {job.DocInstanceId}; ActionCode => {job.ActionCode}; WorkflowStepInstanceId => {job.WorkflowStepInstanceId}");
                            changeProjectStepProgress.Add(new ProjectStepProgress
                            {
                                InstanceId = crrWfsInfo.InstanceId,
                                Name = crrWfsInfo.Name,
                                ActionCode = crrWfsInfo.ActionCode,
                                ProcessingFile = -1,
                                CompleteFile = 1,
                                TotalFile = 0,
                                ProcessingDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() },
                                CompleteDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() }
                            });
                        }

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
                        await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic,
                            accessToken);

                        Log.Logger.Information(
                            $"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile in step {job.ActionCode} with DocInstanceId: {job.DocInstanceId}");

                        // 3.1. Cập nhật giá trị DocFieldValue & Doc
                        itemDocFieldValueUpdateValues.Add(new ItemDocFieldValueUpdateValue
                        {
                            InstanceId = job.DocFieldValueInstanceId.GetValueOrDefault(),
                            Value = job.Value,
                            CoordinateArea = job.CoordinateArea,
                            ActionCode = job.ActionCode
                        });

                        // 4. Trigger bước tiếp theo
                        // 4.1. Tổng hợp dữ liệu itemInputParams
                        var itemInputParams = new List<ItemInputParam>
                    {
                        new ItemInputParam
                        {
                            FilePartInstanceId = job.FilePartInstanceId,
                            //DocTypeFieldId = 0,
                            DocTypeFieldInstanceId = job.DocTypeFieldInstanceId,
                            DocTypeFieldCode = job.DocTypeFieldCode,
                            DocTypeFieldName = job.DocTypeFieldName,
                            DocTypeFieldSortOrder = job.DocTypeFieldSortOrder,
                            InputType = job.InputType,
                            MaxLength = job.MaxLength,
                            MinLength = job.MinLength,
                            MaxValue = job.MaxValue,
                            MinValue = job.MinValue,
                            PrivateCategoryInstanceId = job.PrivateCategoryInstanceId,
                            IsMultipleSelection = job.IsMultipleSelection,
                            CoordinateArea = job.CoordinateArea,
                            DocFieldValueInstanceId = job.DocFieldValueInstanceId,
                            //DocFieldValueId = 0,
                            Value = job.Value
                        }
                    };

                        if (nextWfsInfoes != null && nextWfsInfoes.Any())
                        {
                            if (nextWfsInfoes.All(x => x.ActionCode != ActionCodeConstants.End))
                            {
                                bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                                var isParallelStep =
                                    WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
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

                                    bool isNextStepRequiredAllBeforeStepComplete =
                                        WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes,
                                            nextWfsInfo.InstanceId);

                                    decimal price = 0;
                                    string value = null;
                                    if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                    {
                                        foreach (var itemInput in itemInputParams)
                                        {
                                            // Tổng hợp các thông số cho các bước TIẾP THEO
                                            itemInput.IsDivergenceStep = isDivergenceStep;
                                            itemInput.ParallelJobInstanceId = parallelJobInstanceId;
                                            itemInput.IsConvergenceNextStep = isConvergenceNextStep;

                                            // Tổng hợp price cho các bước TIẾP THEO
                                            if (isMultipleNextStep)
                                            {
                                                itemInput.WorkflowStepPrices = nextWfsInfoes.Select(x =>
                                                    new WorkflowStepPrice
                                                    {
                                                        InstanceId = x.InstanceId,
                                                        ActionCode = x.ActionCode,
                                                        Price = isPaid
                                                            ? MoneyHelper.GetPriceByConfigPriceV2(x.ConfigPrice,
                                                                job.DigitizedTemplateInstanceId,
                                                                itemInput.DocTypeFieldInstanceId)
                                                            : 0
                                                    }).ToList();
                                            }
                                            else
                                            {
                                                itemInput.Price = isPaid
                                                    ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                                        job.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                                    : 0;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        price = isPaid
                                            ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                                job.DigitizedTemplateInstanceId)
                                            : 0;

                                        var crrWfsJobsComplete = await _repository.GetJobByWfs(
                                            job.DocInstanceId.GetValueOrDefault(), crrWfsInfo.ActionCode,
                                            crrWfsInfo.InstanceId, (short)EnumJob.Status.Complete);
                                        if (crrWfsJobsComplete != null && crrWfsJobsComplete.Any())
                                        {
                                            var docItems = crrWfsJobsComplete.Select(x => new DocItem
                                            {
                                                //DocTypeFieldId = 0,
                                                FilePartInstanceId = x.FilePartInstanceId,
                                                DocTypeFieldInstanceId = x.DocTypeFieldInstanceId,
                                                DocTypeFieldName = x.DocTypeFieldName,
                                                DocTypeFieldSortOrder = x.DocTypeFieldSortOrder,
                                                InputType = x.InputType,
                                                MaxLength = x.MaxLength,
                                                MinLength = x.MinLength,
                                                MaxValue = x.MaxValue,
                                                MinValue = x.MinValue,
                                                PrivateCategoryInstanceId = x.PrivateCategoryInstanceId,
                                                IsMultipleSelection = x.IsMultipleSelection,
                                                CoordinateArea = x.CoordinateArea,
                                                //DocFieldValueId = 0,
                                                DocFieldValueInstanceId = x.DocFieldValueInstanceId,
                                                Value = x.Value
                                            }).ToList();

                                            if (isNextStepRequiredAllBeforeStepComplete)
                                            {
                                                var fullDocItemForDoc = new List<DocItem>();

                                                //use dictionary to store docItem by template for reduce call service
                                                if (dicDocItemByTemplate.ContainsKey(job.DigitizedTemplateInstanceId.GetValueOrDefault()))
                                                {
                                                    fullDocItemForDoc = dicDocItemByTemplate[job.DigitizedTemplateInstanceId.GetValueOrDefault()];
                                                }
                                                else
                                                {
                                                    var docTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(job.ProjectInstanceId.GetValueOrDefault(), job.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                                                    if (docTypeFieldResponse.Success && docTypeFieldResponse.Data != null)
                                                    {
                                                        fullDocItemForDoc = _docTypeFieldClientService.ConvertToDocItem(docTypeFieldResponse.Data);
                                                        dicDocItemByTemplate.Add(job.DigitizedTemplateInstanceId.GetValueOrDefault(), fullDocItemForDoc);
                                                    }
                                                }

                                                if (fullDocItemForDoc != null && fullDocItemForDoc != null &&
                                                    fullDocItemForDoc.Count > 0)
                                                {
                                                    var existDocTypeFieldInstanceId = crrWfsJobsComplete
                                                        .Select(x => x.DocTypeFieldInstanceId).ToList();
                                                    var missDocItem = fullDocItemForDoc.Where(x =>
                                                            !existDocTypeFieldInstanceId.Contains(x.DocTypeFieldInstanceId))
                                                        .ToList();
                                                    if (missDocItem != null && missDocItem.Count > 0)
                                                    {
                                                        docItems.AddRange(missDocItem);
                                                    }
                                                }
                                            }

                                            value = JsonConvert.SerializeObject(docItems);
                                        }
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
                                        //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                                        //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                                        Value = value,
                                        Price = price,
                                        //WorkflowStepPrices = null,
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
                                        Input = JsonConvert
                                            .SerializeObject(output), // output của bước trước là input của bước sau
                                        AccessToken = accessToken
                                    };

                                    bool isTriggerNextStep = false;
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

                                                var parallelJobs =
                                                    await _repository
                                                        .GetJobCompleteByDocFieldValueAndParallelJob(
                                                            job.DocInstanceId.GetValueOrDefault(),
                                                            job.DocFieldValueInstanceId, job.ParallelJobInstanceId);

                                                // Xét trường hợp tất cả parallelJobs cùng done tại 1 thời điểm
                                                bool triggerNextStepHappend =
                                                    await TriggerNextStepHappened(job.DocInstanceId.GetValueOrDefault(),
                                                        job.WorkflowStepInstanceId.GetValueOrDefault(),
                                                        job.DocTypeFieldInstanceId, job.DocFieldValueInstanceId);
                                                if (!triggerNextStepHappend)
                                                {
                                                    var oldValues = parallelJobs.Select(x => x.Value);
                                                    itemInputParams.First().Value =
                                                        JsonConvert.SerializeObject(oldValues);
                                                    itemInputParams.First().IsConvergenceNextStep = true;
                                                    taskEvt.Input = JsonConvert.SerializeObject(output);

                                                    await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                                    isTriggerNextStep = true;
                                                }

                                            }
                                        }
                                        else if (isNextStepRequiredAllBeforeStepComplete)
                                        {
                                            // Nếu bước TIẾP THEO yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
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
                                        // Update current wfs status is complete
                                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, job.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                        if (!resultDocChangeCurrentWfsInfo.Success)
                                        {
                                            Log.Logger.Error($"{nameof(AfterProcessDataConfirmProcessEvent)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                                        }
                                        Log.Logger.Information(
                                            $"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {job.DocInstanceId}, JobCode: {job.Code}");
                                    }
                                }
                            }
                            else
                            {
                                // đây là bước cuối cùng: nextstep = end
                                var nextWfsInfo = nextWfsInfoes.First();
                                jobEnds.Add(job);

                                // Update value TaskStepProgress
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
                                await _taskRepository.UpdateProgressValue(job.TaskId, updateTaskStepProgress,
                                    (short)EnumTask.Status.Complete);

                                // Update ProjectStatistic
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
                                    StatisticDate = Int32.Parse(job.DocCreatedDate.GetValueOrDefault().Date
                                        .ToString("yyyyMMdd")),
                                    ChangeFileProgressStatistic =
                                        JsonConvert.SerializeObject(changeProjectFileProgressEnd),
                                    ChangeStepProgressStatistic =
                                        JsonConvert.SerializeObject(changeProjectStepProgressEnd),
                                    ChangeUserStatistic = string.Empty,
                                    TenantId = job.TenantId
                                };
                                await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatisticEnd,
                                    accessToken);

                                Log.Logger.Information(
                                    $"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile in step {job.ActionCode} with DocInstanceId: {job.DocInstanceId}");
                            }
                        }
                    }

                    //Cập nhật RightStatus
                    if (lstJobEntryCheckWrong.Any() || lstJobEntryCheckRight.Any())
                    {
                        var lstJobEntryChecked = lstJobEntryCheckWrong.Union(lstJobEntryCheckRight);
                        await _repository.UpdateMultiAsync(lstJobEntryChecked);
                    }

                    // 1.2. Cập nhật thanh toán tiền cho worker bước HIỆN TẠI (DataConfirm)
                    if (itemTransactionAdds.Any())
                    {
                        var transactionAddMulti = new TransactionAddMultiDto
                        {
                            CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Kiểm tra",
                                string.Join(", ", completeJobCodes)),
                            CorrelationDescription = $"Hoàn thành các công việc {string.Join(", ", completeJobCodes)}",
                            ItemTransactionAdds = itemTransactionAdds,
                            ItemTransactionToSysWalletAdds = itemTransactionToSysWalletAdds
                        };
                        await _transactionClientService.AddMultiTransactionAsync(transactionAddMulti, accessToken);
                    }

                    // 3.2. Cập nhật giá trị DocFieldValue & Doc

                    // 4.2. Sau bước HIỆN TẠI là End (ko có bước SyntheticData) thì cập nhật FinalValue cho Doc và chuyển all trạng thái DocFieldValues sang Complete
                    if (jobEnds.Any())
                    {
                        var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(jobEnds[0].ProjectInstanceId.GetValueOrDefault(), jobEnds[0].DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                        if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                        {
                            Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                            return new Tuple<bool, string, string>(false,
                                "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                null);
                        }

                        var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();
                    
                        var docInstanceIds = jobEnds.Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct().ToList();
                        foreach (var docInstanceId in docInstanceIds)
                        {
                            var actionCode = jobEnds.FirstOrDefault(x => x.DocInstanceId == docInstanceId)?.ActionCode;
                            var wfsInstanceId = jobEnds.FirstOrDefault(x => x.DocInstanceId == docInstanceId)
                                ?.WorkflowStepInstanceId;
                            bool hasJobWaitingOrProcessing =
                                await _repository.CheckHasJobWaitingOrProcessingByIgnoreWfs(docInstanceId, actionCode,
                                    wfsInstanceId, ignoreListDocTypeField);

                            var wfInstanceId = jobEnds.FirstOrDefault(x => x.DocInstanceId == docInstanceId)?.WorkflowInstanceId;
                            var crrWfInfoes = await GetWfInfoes(wfInstanceId.GetValueOrDefault(), accessToken);
                            var crrWfsInfoes = crrWfInfoes.Item1;
                            var crrWfSchemaInfoes = crrWfInfoes.Item2;
                            var crrWfsInfo = crrWfsInfoes.First(x => x.InstanceId == wfsInstanceId);
                            if (!hasJobWaitingOrProcessing)
                            {
                                // Get lại toàn bộ job trong bước HIỆN TẠI đã Complete
                                var crrJobsComplete = await _repository.GetJobByWfs(docInstanceId, actionCode,
                                    wfsInstanceId, (short)EnumJob.Status.Complete);
                                var docItems = crrJobsComplete.Select(x => new DocItem
                                {
                                    //DocTypeFieldId = 0,
                                    FilePartInstanceId = x.FilePartInstanceId,
                                    DocTypeFieldInstanceId = x.DocTypeFieldInstanceId,
                                    DocTypeFieldName = x.DocTypeFieldName,
                                    DocTypeFieldSortOrder = x.DocTypeFieldSortOrder,
                                    InputType = x.InputType,
                                    MaxLength = x.MaxLength,
                                    MinLength = x.MinLength,
                                    MaxValue = x.MaxValue,
                                    MinValue = x.MinValue,
                                    PrivateCategoryInstanceId = x.PrivateCategoryInstanceId,
                                    IsMultipleSelection = x.IsMultipleSelection,
                                    CoordinateArea = x.CoordinateArea,
                                    //DocFieldValueId = 0,
                                    DocFieldValueInstanceId = x.DocFieldValueInstanceId,
                                    Value = x.Value
                                }).ToList();
                                var finalValue = JsonConvert.SerializeObject(docItems);

                                // Update FinalValue for Doc
                                var docUpdateFinalValueEvt = new DocUpdateFinalValueEvent
                                {
                                    DocInstanceId = docInstanceId,
                                    FinalValue = finalValue
                                };
                                // Outbox
                                var outboxEntity = await _outboxIntegrationEventRepository.AddAsyncV2(
                                    new OutboxIntegrationEvent
                                    {
                                        ExchangeName = nameof(DocUpdateFinalValueEvent).ToLower(),
                                        ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                                        Data = JsonConvert.SerializeObject(docUpdateFinalValueEvt)
                                    });
                                var isAck = _eventBus.Publish(docUpdateFinalValueEvt,
                                    nameof(DocUpdateFinalValueEvent).ToLower());
                                if (isAck)
                                {
                                    await _outboxIntegrationEventRepository.DeleteAsync(outboxEntity);
                                }
                                else
                                {
                                    outboxEntity.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                                    await _outboxIntegrationEventRepository.UpdateAsync(outboxEntity);
                                }


                                // Update current wfs status is complete
                                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(docInstanceId, crrWfsInfo.Id, (short)EnumJob.Status.Complete, null, null, "", null, accessToken: accessToken);
                                if (!resultDocChangeCurrentWfsInfo.Success)
                                {
                                    Log.Logger.Error($"{nameof(AfterProcessDataConfirmProcessEvent)}: Error change current work flow step info for DocInstanceId: {docInstanceId}!");
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
            var isAck = _eventBus.Publish(evt,
                isNextStepHeavyJob ? EventBusConstants.EXCHANGE_HEAVY_JOB : nameof(TaskEvent).ToLower());
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

        private async Task<bool> TriggerNextStepHappened(Guid docInstanceId, Guid workflowStepInstanceId,
            Guid? docTypeFieldInstanceId = null, Guid? docFieldValueInstanceId = null)
        {
            if (_useCache)
            {
                var strDocTypeFieldInstanceId = docTypeFieldInstanceId == null
                    ? "null"
                    : docTypeFieldInstanceId.ToString();
                var strDocFieldValueInstanceId = docFieldValueInstanceId == null
                    ? "null"
                    : docFieldValueInstanceId.ToString();

                string cacheKey =
                    $"{docInstanceId}_{workflowStepInstanceId}_{strDocTypeFieldInstanceId}_{strDocFieldValueInstanceId}";
                var triggerNextStepHappened =
                    await _cachingHelper.TryGetFromCacheAsync<string>(cacheKey); // Lưu số lần trigger
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

        private async Task<Tuple<List<WorkflowStepInfo>, List<WorkflowSchemaConditionInfo>>> GetWfInfoes(
            Guid workflowInstanceId, string accessToken = null)
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

        #region Enrich data for InputParam

        private async Task EnrichDataJob(AfterProcessDataConfirmEvent evt)
        {
            if (evt.Jobs == null || evt.Jobs.Count == 0)
            {
                if (evt.JobIds != null && evt.JobIds.Any())
                {
                    var jobs = (await _repository.GetByIdsAsync(JsonConvert.SerializeObject(evt.JobIds))).ToList();
                    evt.Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs);
                }
            }
        }

        #endregion
    }
}
