using AutoMapper;
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
using MongoDB.Bson;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent
{
    public interface IAfterProcessCheckFinalProcessEvent : IBaseInboxProcessEvent<AfterProcessCheckFinalEvent>, IDisposable { }

    public class AfterProcessCheckFinalProcessEvent : Disposable, IAfterProcessCheckFinalProcessEvent
    {
        private readonly IJobRepository _repository;
        private readonly ITaskRepository _taskRepository;
        private readonly IEventBus _eventBus;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IMoneyService _moneyService;
        private readonly IDocClientService _docClientService;
        private readonly IBatchClientService _batchClientService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;
        private readonly IDocTypeFieldClientService _docTypeFieldClientService;

        public AfterProcessCheckFinalProcessEvent(
            IJobRepository repository,
            ITaskRepository taskRepository,
            IEventBus eventBus,
            IWorkflowClientService workflowClientService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IMoneyService moneyService,
            IDocClientService docClientService,
            IBatchClientService batchClientService,
            IServiceProvider provider,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration, IMapper mapper, IDocTypeFieldClientService docTypeFieldClientService)
        {
            _repository = repository;
            _taskRepository = taskRepository;
            _eventBus = eventBus;
            _workflowClientService = workflowClientService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _moneyService = moneyService;
            _docClientService = docClientService;
            _batchClientService = batchClientService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _mapper = mapper;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
            _docTypeFieldClientService = docTypeFieldClientService;
        }

        public async Task<Tuple<bool, string, string>> ProcessEvent(AfterProcessCheckFinalEvent evt, CancellationToken ct = default)
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
                    var methodName = "ProcessAfterProcessCheckFinal";
                    var sw = Stopwatch.StartNew();
                    string accessToken = evt.AccessToken;

                    await EnrichDataJob(evt);

                    var job = evt.Job;
                    var userInstanceId = job.UserInstanceId.GetValueOrDefault();
                    var jobEnds = new List<JobDto>();

                    if (string.IsNullOrEmpty(job.Value))
                    {
                        Log.Error("ProcessCheckFinal value of job is null!");
                        return new Tuple<bool, string, string>(false, "ProcessCheckFinal value of job is null!", null);
                    }

                    var docItems = JsonConvert.DeserializeObject<List<DocItem>>(job.Value);
                    if (docItems == null || docItems.Count <= 0)
                    {
                        Log.Error("ProcessCheckFinal value of job can not be parse!");
                        return new Tuple<bool, string, string>(false, "ProcessCheckFinal value of job can not be parse!", null);
                    }

                    var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                    var wfsInfoes = wfInfoes.Item1;
                    var wfSchemaInfoes = wfInfoes.Item2;

                    if (wfsInfoes == null || wfsInfoes.Count <= 0)
                    {
                        Log.Error("ProcessCheckFinal can not get wfsInfoes!");
                        return new Tuple<bool, string, string>(false, "ProcessCheckFinal can not get wfsInfoes!", null);
                    }

                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);

                    // 1. Cập nhật thanh toán tiền cho worker & hệ thống// Cập nhật thanh toán tiền cho worker bước HIỆN TẠI (CheckFinal) => Old
                    // 1. Cập nhật tiền TẠM TÍNH cho worker => New
                    var clientInstanceId = await GetClientInstanceIdByProject(job.ProjectInstanceId.GetValueOrDefault(), accessToken);
                    if (clientInstanceId != Guid.Empty)
                    {
                        #region Bussiness Old

                        //var itemTransactionAdds = new List<ItemTransactionAddDto>
                        //{
                        //    new ItemTransactionAddDto
                        //    {
                        //        SourceUserInstanceId = clientInstanceId,
                        //        DestinationUserInstanceId = userInstanceId,
                        //        ChangeAmount = job.Price * (100 - job.ClientTollRatio) / 100,
                        //        ChangeProvisionalAmount = 0,
                        //        JobCode = job.Code,
                        //        ProjectInstanceId = job.ProjectInstanceId,
                        //        WorkflowInstanceId = job.WorkflowInstanceId,
                        //        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        //        ActionCode = job.ActionCode,
                        //        Message =
                        //            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        //        Description = string.Format(
                        //            DescriptionTransactionTemplate.DescriptionTranferMoneyForCompleteJob,
                        //            clientInstanceId, userInstanceId,
                        //            job.Code)
                        //    }
                        //};
                        //var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                        //if (job.ClientTollRatio > 0)
                        //{
                        //    itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                        //    {
                        //        SourceUserInstanceId = clientInstanceId,
                        //        ChangeAmount = job.Price * job.ClientTollRatio / 100,
                        //        JobCode = job.Code,
                        //        ProjectInstanceId = job.ProjectInstanceId,
                        //        WorkflowInstanceId = job.WorkflowInstanceId,
                        //        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        //        ActionCode = job.ActionCode,
                        //        Message =
                        //            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        //        Description =
                        //            string.Format(
                        //                DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                        //                clientInstanceId, job.Code)
                        //    });
                        //}
                        //if (job.WorkerTollRatio > 0)
                        //{
                        //    itemTransactionToSysWalletAdds.Add(new ItemTransactionToSysWalletAddDto
                        //    {
                        //        SourceUserInstanceId = userInstanceId,
                        //        ChangeAmount = (job.Price * (100 - job.ClientTollRatio) / 100) * job.WorkerTollRatio / 100,
                        //        JobCode = job.Code,
                        //        ProjectInstanceId = job.ProjectInstanceId,
                        //        WorkflowInstanceId = job.WorkflowInstanceId,
                        //        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        //        ActionCode = job.ActionCode,
                        //        Message =
                        //            string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code),
                        //        Description = string.Format(
                        //            DescriptionTransactionTemplate.DescriptionTranferMoneyToSysWalletForCompleteJob,
                        //            userInstanceId, job.Code)
                        //    });
                        //}

                        //var transactionAddMulti = new TransactionAddMultiDto
                        //{
                        //    CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Xác nhận",
                        //        job.Code),
                        //    CorrelationDescription = $"Hoàn thành các công việc {job.Code}",
                        //    ItemTransactionAdds = itemTransactionAdds,
                        //    ItemTransactionToSysWalletAdds = itemTransactionToSysWalletAdds
                        //};

                        #endregion

                        #region Bussiness New

                        var itemTransactionAdds = new List<ItemTransactionAddDto>
                {
                    new ItemTransactionAddDto
                    {
                        SourceUserInstanceId = clientInstanceId,
                        DestinationUserInstanceId = userInstanceId,
                        ChangeAmount = 0,
                        ChangeProvisionalAmount = job.Price,
                        JobCode = job.Code,
                        ProjectInstanceId = job.ProjectInstanceId,
                        WorkflowInstanceId = job.WorkflowInstanceId,
                        WorkflowStepInstanceId = job.WorkflowStepInstanceId,
                        ActionCode = job.ActionCode,
                        Message = $"Tạm tính {string.Format(MsgTransactionTemplate.MsgJobInfoes, crrWfsInfo?.Name, job.Code)}",
                        Description = string.Format(
                            DescriptionTransactionTemplateV2.DescriptionIncreaseProvisionalMoneyForCompleteJob,
                            userInstanceId,
                            crrWfsInfo.Name,
                            job.Code)
                    }
                };
                        var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                        var transactionAddMulti = new TransactionAddMultiDto
                        {
                            CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Xác nhận",
                                job.Code),
                            CorrelationDescription = $"Hoàn thành các công việc {job.Code}",
                            ItemTransactionAdds = itemTransactionAdds,
                            ItemTransactionToSysWalletAdds = itemTransactionToSysWalletAdds
                        };

                        #endregion

                        await _transactionClientService.AddMultiTransactionAsync(transactionAddMulti, accessToken);
                    }
                    else
                    {
                        Log.Logger.Error($"Can not get ClientInstanceId from ProjectInstanceId: {job.ProjectInstanceId}!");
                    }

                    Log.Logger.Information($"ProcessCheckFinal: {job.Code} with DocInstanceId: {job.DocInstanceId} success!");

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
                        UserInstanceIds = new List<Guid> { userInstanceId }
                    }
                }
                    };
                    // Nếu ko tồn tại job Waiting hoặc Processing ở các bước TRƯỚC và bước HIỆN TẠI thì mới chuyển trạng thái
                    var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault(), true);
                    // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                    var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(job.ProjectInstanceId.GetValueOrDefault(), job.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                    if (listDocTypeFieldResponse.Success == false)
                    {
                        throw new Exception("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                    }

                    var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                    var hasJobWaitingOrProcessing = await _repository.CheckHasJobWaitingOrProcessingByMultiWfs(job.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                    if (!hasJobWaitingOrProcessing)
                    {
                        Log.Information($"ProcessCheckFinal change step: DocInstanceId => {job.DocInstanceId}; ActionCode => {job.ActionCode}; WorkflowStepInstanceId => {job.WorkflowStepInstanceId}");
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
                    docItems.ForEach(x =>
                    {
                        itemDocFieldValueUpdateValues.Add(new ItemDocFieldValueUpdateValue
                        {
                            InstanceId = x.DocFieldValueInstanceId.GetValueOrDefault(),
                            Value = x.Value,
                            CoordinateArea = x.CoordinateArea,
                            ActionCode = job.ActionCode
                        });
                    });

                    if (itemDocFieldValueUpdateValues.Any())
                    {
                        var docFieldValueUpdateMultiValueEvt = new DocFieldValueUpdateMultiValueEvent
                        {
                            ItemDocFieldValueUpdateValues = itemDocFieldValueUpdateValues
                        };
                        // Outbox
                        var outboxEntity = new OutboxIntegrationEvent
                        {
                            ExchangeName = nameof(DocFieldValueUpdateMultiValueEvent).ToLower(),
                            ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                            Data = JsonConvert.SerializeObject(docFieldValueUpdateMultiValueEvt),
                            LastModificationDate = DateTime.Now,
                            Status = (short)EnumEventBus.PublishMessageStatus.Nack
                        };

                        try //try to publish event
                        {
                            _eventBus.Publish(docFieldValueUpdateMultiValueEvt, nameof(DocFieldValueUpdateMultiValueEvent).ToLower());
                        }
                        catch (Exception exPublishEvent)
                        {
                            Log.Error(exPublishEvent, "Error publish for event DocFieldValueUpdateMultiValueEvent");

                            try // try to save event to DB for retry later
                            {
                                await _outboxIntegrationEventRepository.AddAsync(outboxEntity);
                            }
                            catch (Exception exSaveDB)
                            {
                                Log.Error(exSaveDB, "Error save DB for event DocFieldValueUpdateMultiValueEvent");
                                throw;
                            }
                        }
                    }

                    // 4. Trigger bước tiếp theo
                    // Tổng hợp dữ liệu itemInputParams
                    var itemInputParams = new List<ItemInputParam>
            {
                new ItemInputParam
                {
                    FilePartInstanceId = job.FilePartInstanceId,
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
                    Value = job.Value
                }
            };

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

                                bool isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                                bool isNextStepQa = nextWfsInfo.ActionCode == ActionCodeConstants.QACheckFinal;

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
                                            itemInput.WorkflowStepPrices = nextWfsInfoes.Select(x => new WorkflowStepPrice
                                            {
                                                InstanceId = x.InstanceId,
                                                ActionCode = x.ActionCode,
                                                Price = isPaid
                                                    ? MoneyHelper.GetPriceByConfigPriceV2(x.ConfigPrice,
                                                        job.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
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
                                    value = JsonConvert.SerializeObject(docItems);
                                    price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
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
                                    //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                                    //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                                    Value = value,
                                    Price = price,
                                    ClientTollRatio = job.ClientTollRatio,
                                    WorkerTollRatio = job.WorkerTollRatio,
                                    IsDivergenceStep = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                       isMultipleNextStep,
                                    ParallelJobInstanceId =
                                        nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                            ? parallelJobInstanceId
                                            : null,
                                    IsConvergenceNextStep =
                                        nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                        isConvergenceNextStep,
                                    NumOfRound = job.NumOfRound,
                                    BatchName = job.BatchName,
                                    BatchJobInstanceId = job.BatchJobInstanceId,
                                    TenantId = job.TenantId,
                                    ItemInputParams = itemInputParams
                                };

                                if (output.BatchJobInstanceId == null)
                                {
                                    if (isNextStepQa)
                                    {
                                        output.BatchJobInstanceId = Guid.Empty; //quan trọng: gắn = empty để phân biệt File đã hoàn thành và chuyển sang QA
                                    }
                                }
                                var taskEvt = new TaskEvent
                                {
                                    Input = JsonConvert.SerializeObject(output),     // output của bước trước là input của bước sau
                                    AccessToken = accessToken
                                };

                                bool isTriggerNextStep = false;
                                bool isProcessQAInBatchMode = false; // kiểm tra xem xử lý QA theo lô hay theo file đơn lẻ

                                var batchQASize = 0; //  Số phiếu / lô ; nếu = 0 thì cả thư mục là 1 lô
                                var batchQASampling = 100;  // % lấy mẫu trong lô 
                                var batchQAFalseThreshold = 100; // ngưỡng sai: nếu >= % ngưỡng thì trả lại cả lô

                                if (isNextStepQa)
                                {
                                    var configQa = WorkflowHelper.GetConfigQa(nextWfsInfo.ConfigStep);
                                    batchQASize = configQa.Item2; // Số phiếu / lô ; nếu = 0 thì cả thư mục là 1 lô
                                    batchQASampling = configQa.Item3; // % lấy mẫu trong lô 
                                    batchQAFalseThreshold = configQa.Item4; // ngưỡng sai: nếu >= % ngưỡng thì trả lại cả lô
                                    isProcessQAInBatchMode = configQa.Item1;


                                    //Tạm fix: nếu file upload không vào thư mục nào thì chạy theo chế độ QA đơn file
                                    if (string.IsNullOrEmpty(job.DocPath))
                                    {
                                        isProcessQAInBatchMode = false;
                                    }
                                }

                                //nếu bước tiếp theo là QA theo batch mode hoặc là các bước yêu cầu cùng hoàn thành đồng thời
                                if ((isNextStepQa && isProcessQAInBatchMode) || isNextStepRequiredAllBeforeStepComplete || job.IsParallelJob)
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
                                            if (parallelJobs.Count ==
                                                countOfExpectParallelJobs) // Số lượng parallelJobs bằng với countOfExpectParallelJobs thì mới next step
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
                                    else if (isNextStepQa && isProcessQAInBatchMode)
                                    {
                                        /*
                                         * Rule => Mỗi lô chỉ chứa job CheckFinal của 1 người
                                         * 1. Trường hợp Chưa có Lô
                                         * 1.1. Đủ BatchSize thì TriggerNextStep
                                         * 1.2. Chưa đủ BatchSize (không tròn Lô) => Tổng số jobs Complete = (Tổng số files trong path - Tổng file đã tạo lô QA) =>  thì TriggerNextStep
                                         * 2. Trường hợp Đã có Lô rồi => phải đợi Tất cả các jobs Complete thì TriggerNextStep
                                         * 
                                         */
                                        var isPathUploading = false;
                                        var totalDocInPath = 0;

                                        var pathStatusRs = await _docClientService.GetStatusPath(job.ProjectInstanceId.GetValueOrDefault(), job.SyncTypeInstanceId.GetValueOrDefault(), job.DocPath, accessToken);


                                        if (pathStatusRs.Success && pathStatusRs.Data != null)
                                        {
                                            isPathUploading = pathStatusRs.Data.IsUploading;
                                            totalDocInPath = pathStatusRs.Data.TotalDoc;
                                        }

                                        if (batchQASize == 0) // nếu setting BatQASize=0 thì coi cả thư mục là 1 lô
                                        {
                                            batchQASize = totalDocInPath;
                                        }

                                        // 1. Trường hợp chưa có Lô => tính toán tạo lô
                                        if (job.NumOfRound == 0 && job.BatchJobInstanceId.GetValueOrDefault() == Guid.Empty)
                                        {
                                            // Lấy tất cả các jobs CheckFinal Complete tại Round=0 và chưa có lô
                                            sw.Restart();
                                            var allCompleteJobs = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(),
                                                                                job.ActionCode,
                                                                                job.WorkflowStepInstanceId,
                                                                                (short)EnumJob.Status.Complete,
                                                                                job.DocPath, null, 0);

                                            sw.Stop();
                                            Log.Debug($"{methodName} - allCompleteJobs-GetAllJobByWfs - Elapsed time {sw.ElapsedMilliseconds} ms ");

                                            var total_Job_Round_0_has_batch = allCompleteJobs.Where(x => x.BatchJobInstanceId.GetValueOrDefault() != Guid.Empty).Count();

                                            allCompleteJobs = allCompleteJobs.Where(x => x.BatchJobInstanceId.GetValueOrDefault() == Guid.Empty).ToList();

                                            var isCreateNewBatch = false;


                                            // nếu tất cả các file trong cùng 1 path đã hoàn thành hết thì tạo ra tất cả các lô cho từng user
                                            if (allCompleteJobs.Count == totalDocInPath - total_Job_Round_0_has_batch)
                                            {
                                                isCreateNewBatch = true;
                                            }
                                            else // nếu 
                                            {
                                                //chỉ lấy những việc của người đang submit job hiên tại để tạo lô
                                                allCompleteJobs = allCompleteJobs.Where(x => x.LastModifiedBy == job.LastModifiedBy).ToList();

                                                if (allCompleteJobs.Count == batchQASize)
                                                {
                                                    isCreateNewBatch = true;
                                                }
                                            }

                                            //đủ điề kiện => Tạo lô QA và trigger nextjob
                                            if (isCreateNewBatch)
                                            {
                                                var listUser = allCompleteJobs.GroupBy(x => x.LastModifiedBy).Select(grp => grp.Key.GetValueOrDefault()).ToList();

                                                //Tạo lô cho từng user
                                                foreach (var lastUserInstanceId in listUser)
                                                {
                                                    var allCompleteJobsByUser = allCompleteJobs.Where(x => x.LastModifiedBy == lastUserInstanceId).ToList();

                                                    // Tạo Batch và cập nhật BatchId cho danh sách Docs
                                                    var batchRs = await _batchClientService.CreateBatch(new BatchDto
                                                    {
                                                        Path = job.DocPath,
                                                        ProjectInstanceId = job.ProjectInstanceId.GetValueOrDefault(),
                                                        DocInstanceIds = allCompleteJobsByUser.Select(x => x.DocInstanceId.GetValueOrDefault()).ToList(),
                                                        DocCount = allCompleteJobsByUser.Count
                                                    }, accessToken);

                                                    //đánh dấu các job CF (Round=0; batch=null) = NewBach
                                                    allCompleteJobsByUser.ForEach(x =>
                                                    {
                                                        x.BatchJobInstanceId = batchRs.Data.InstanceId;
                                                        x.BatchName = batchRs.Data.Name;
                                                    });
                                                    await _repository.UpdateMultiAsync(allCompleteJobsByUser);

                                                    //lấy ngẫu nhiên để tạo batch job QA
                                                    var numJobQa = (int)Math.Round(allCompleteJobsByUser.Count * ((decimal)batchQASampling / 100), MidpointRounding.ToEven);
                                                    if (numJobQa < 1)
                                                    {
                                                        numJobQa = 1;
                                                    }
                                                    var listJobQa = allCompleteJobsByUser.OrderBy(x => x.RandomSortOrder).Take(numJobQa).ToList();
                                                    // Re-serialize taskEvt
                                                    output.InputParams = CreateInputParamForNextJob(listJobQa, wfsInfoes, wfSchemaInfoes, nextWfsInfo, parallelJobInstanceId, isMultipleNextStep, isConvergenceNextStep);
                                                    taskEvt.Input = JsonConvert.SerializeObject(output);
                                                    await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                                    isTriggerNextStep = true;

                                                }
                                            }
                                        }

                                        // 2. Trường hợp Đã có Lô rồi => Hiệu chỉnh Batchsize = số lượng file trong lô
                                        else
                                        {
                                            // Lấy jobs CheckFinal (có Status=ALL) của Batch + Round hiện tại
                                            var allJobInBatch = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(),
                                                job.ActionCode,
                                                job.WorkflowStepInstanceId, null, job.DocPath,
                                                job.BatchJobInstanceId, job.NumOfRound);

                                            var totalJobInBatch = allJobInBatch.Count;
                                            var completedJobInBatch = allJobInBatch.Count(x => x.Status == (short)EnumJob.Status.Complete);


                                            batchQASize = totalJobInBatch;

                                            //Logic:  phải đợi Tất cả các jobs CheckFinal Complete thì TriggerNextStep
                                            if (completedJobInBatch == totalJobInBatch)
                                            {
                                                // tạo lượt QA mới => gồm các phiếu bị QA fail ở round hiện tại -> ưu tiên lấy các fiel bị QA=False ở Round trước
                                                var listQAJobInLastRound = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(),
                                                    nextWfsInfo.ActionCode,
                                                nextWfsInfo.InstanceId, (short)EnumJob.Status.Complete, job.DocPath,
                                                job.BatchJobInstanceId, (short)(job.NumOfRound - 1));

                                                var numJobQa = (int)Math.Round(batchQASize * (decimal)batchQASampling / 100, MidpointRounding.ToEven);

                                                listQAJobInLastRound = listQAJobInLastRound.Where(x => x.QaStatus == false).ToList();

                                                //ưu tiên lấy các File bị QA False ở vòng trước => listJobQA
                                                var listJobQa = allJobInBatch.Where(x => listQAJobInLastRound.Any(y => y.DocInstanceId == x.DocInstanceId)).ToList();

                                                //nếu chưa đủ số lượng lô QA -> thì lấy thêm
                                                if (listJobQa.Count < numJobQa)
                                                {
                                                    var moreNumber = numJobQa - listJobQa.Count();

                                                    //lấy thêm các file khác mã chưa tồn tại trong listJobQA
                                                    var listJobQaMore = allJobInBatch.Where(x => listJobQa.Any(y => y.DocInstanceId == x.DocInstanceId) == false).OrderBy(x => x.RandomSortOrder).Take(moreNumber).ToList();
                                                    listJobQa.AddRange(listJobQaMore);
                                                }

                                                // Re-serialize taskEvt
                                                output.InputParams = CreateInputParamForNextJob(listJobQa, wfsInfoes, wfSchemaInfoes, nextWfsInfo, parallelJobInstanceId, isMultipleNextStep, isConvergenceNextStep);
                                                taskEvt.Input = JsonConvert.SerializeObject(output);
                                                await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);
                                                isTriggerNextStep = true;
                                            }
                                        }
                                    }
                                }
                                else //nếu là ( QA nhưng đơn file ) hoặc ( các điều kiện khác ) thì next step luôn
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
                            await _taskRepository.UpdateProgressValue(job.TaskId, updateTaskStepProgress, (short)EnumTask.Status.Complete);

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
                                StatisticDate = Int32.Parse(job.DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                                ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgressEnd),
                                ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgressEnd),
                                ChangeUserStatistic = string.Empty,
                                TenantId = job.TenantId
                            };
                            await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatisticEnd, accessToken);

                            Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 CompleteFile in step {job.ActionCode} with DocInstanceId: {job.DocInstanceId}");
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

                            // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                            bool hasJobWaitingOrProcessingByIgnoreWfs =
                                await _repository.CheckHasJobWaitingOrProcessingByIgnoreWfs(docInstanceId, actionCode, wfsIntanceId, ignoreListDocTypeField);

                            if (!hasJobWaitingOrProcessingByIgnoreWfs)
                            {
                                // Update FinalValue for Doc
                                var finalValue = JsonConvert.SerializeObject(docItems);
                                var docUpdateFinalValueEvt = new DocUpdateFinalValueEvent
                                {
                                    DocInstanceId = docInstanceId,
                                    FinalValue = finalValue
                                };
                                // Outbox
                                var outboxEntity = new OutboxIntegrationEvent
                                {
                                    ExchangeName = nameof(DocUpdateFinalValueEvent).ToLower(),
                                    ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                                    Data = JsonConvert.SerializeObject(docUpdateFinalValueEvt),
                                    LastModificationDate = DateTime.Now,
                                    Status = (short)EnumEventBus.PublishMessageStatus.Nack
                                };

                                try // try to publish event
                                {
                                    _eventBus.Publish(docUpdateFinalValueEvt, nameof(DocUpdateFinalValueEvent).ToLower());

                                }
                                catch (Exception exPublishEvent)
                                {
                                    Log.Error(exPublishEvent, "Error publish for event DocUpdateFinalValueEvent");

                                    try // try to save event to DB for retry later
                                    {
                                        await _outboxIntegrationEventRepository.AddAsync(outboxEntity);

                                    }
                                    catch (Exception exSaveDB)
                                    {
                                        Log.Error(exSaveDB, "Error save DB for event DocUpdateFinalValueEvent");
                                        throw;
                                    }
                                }

                                // Update all status DocFieldValues is complete
                                var docFieldValueUpdateStatusCompleteEvt = new DocFieldValueUpdateStatusCompleteEvent
                                {
                                    DocFieldValueInstanceIds = docItems
                                        .Select(x => x.DocFieldValueInstanceId.GetValueOrDefault()).ToList()
                                };
                                // Outbox
                                var outboxEntityDocFieldValueUpdateStatusCompleteEvent = new OutboxIntegrationEvent
                                {
                                    ExchangeName = nameof(DocFieldValueUpdateStatusCompleteEvent).ToLower(),
                                    ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                                    Data = JsonConvert.SerializeObject(docFieldValueUpdateStatusCompleteEvt),
                                    LastModificationDate = DateTime.Now,
                                    Status = (short)EnumEventBus.PublishMessageStatus.Nack
                                };

                                try // try to publish event
                                {
                                    _eventBus.Publish(docFieldValueUpdateStatusCompleteEvt, nameof(DocFieldValueUpdateStatusCompleteEvent).ToLower());
                                }
                                catch (Exception exPublishEvent)
                                {
                                    Log.Error(exPublishEvent, "Error publish for event docFieldValueUpdateStatusCompleteEvt");

                                    try // try to save event to DB for retry later
                                    {
                                        await _outboxIntegrationEventRepository.AddAsync(outboxEntityDocFieldValueUpdateStatusCompleteEvent);
                                    }
                                    catch (Exception exSaveDB)
                                    {
                                        Log.Error(exSaveDB, "Error save DB for event docFieldValueUpdateStatusCompleteEvt");
                                        throw;
                                    }
                                }

                                await _moneyService.ChargeMoneyForCompleteDoc(wfsInfoes, wfSchemaInfoes, docItems, docInstanceId, accessToken);
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

        private List<InputParam> CreateInputParamForNextJob(List<Model.Entities.Job> listJob, List<WorkflowStepInfo> wfsInfoes, List<WorkflowSchemaConditionInfo> wfSchemaInfoes, WorkflowStepInfo nextWfsInfo, Guid parallelJobInstanceId, bool isMultipleNextStep, bool isConvergenceNextStep)
        {
            var result = listJob.Select(j => new InputParam
            {
                FileInstanceId = j.FileInstanceId,
                ActionCode = nextWfsInfo.ActionCode,
                //ActionCodes = null,
                DocInstanceId = j.DocInstanceId,
                DocName = j.DocName,
                DocCreatedDate = j.DocCreatedDate,
                DocPath = j.DocPath,
                TaskId = j.TaskId.ToString(),
                TaskInstanceId = j.TaskInstanceId,
                ProjectTypeInstanceId = j.ProjectTypeInstanceId,
                ProjectInstanceId = j.ProjectInstanceId,
                SyncTypeInstanceId = j.SyncTypeInstanceId,
                DigitizedTemplateInstanceId = j.DigitizedTemplateInstanceId,
                DigitizedTemplateCode = j.DigitizedTemplateCode,
                WorkflowInstanceId = j.WorkflowInstanceId,
                WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                //WorkflowStepInstanceIds = null,
                //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                Value = j.Value,
                Price = j.Price,
                ClientTollRatio = j.ClientTollRatio,
                WorkerTollRatio = j.WorkerTollRatio,
                IsDivergenceStep = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                               isMultipleNextStep,
                ParallelJobInstanceId =
                                                nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                                    ? parallelJobInstanceId
                                                    : null,
                IsConvergenceNextStep =
                                                nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                isConvergenceNextStep,
                NumOfRound = j.NumOfRound,
                BatchName = j.BatchName,
                BatchJobInstanceId = j.BatchJobInstanceId,
                TenantId = j.TenantId,
                //ItemInputParams = itemInputParams
            }).ToList();

            return result;
        }

        private async Task TriggerNextStep(TaskEvent evt, string nextWfsActionCode)
        {
            bool isNextStepHeavyJob = WorkflowHelper.IsHeavyJob(nextWfsActionCode);
            var exchangeName = isNextStepHeavyJob ? EventBusConstants.EXCHANGE_HEAVY_JOB : nameof(TaskEvent).ToLower();
            // Outbox
            var outboxEntity = new OutboxIntegrationEvent
            {
                ExchangeName = exchangeName,
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(evt),
                LastModificationDate = DateTime.Now,
                Status = (short)EnumEventBus.PublishMessageStatus.Nack,
            };

            try //try to publish
            {
                _eventBus.Publish(evt, exchangeName);
            }
            catch (Exception exPublishEvent)
            {
                Log.Error(exPublishEvent, $"Error publish for event {exchangeName}");

                try //try to save event to DB for retry later
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

        #region Enrich data for InputParam

        private async Task EnrichDataJob(AfterProcessCheckFinalEvent evt)
        {
            if (evt.Job == null)
            {
                if (!string.IsNullOrEmpty(evt.JobId))
                {
                    var crrJob = await _repository.GetByIdAsync(new ObjectId(evt.JobId));
                    evt.Job = _mapper.Map<Job, JobDto>(crrJob);
                }
            }
        }

        #endregion
    }
}
