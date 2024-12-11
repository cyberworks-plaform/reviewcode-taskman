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
using Axe.Utility.MessageTemplate;
using Azure.Core;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.EventBusRabbitMq.Lib.Interfaces;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent
{
    public interface IAfterProcessQaCheckFinalProcessEvent : IBaseInboxProcessEvent<AfterProcessQaCheckFinalEvent>, IDisposable { }

    public class AfterProcessQaCheckFinalProcessEvent : Disposable, IAfterProcessQaCheckFinalProcessEvent
    {
        private readonly IJobRepository _repository;
        private readonly ITaskRepository _taskRepository;
        private readonly IEventBus _eventBus;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IJobService _jobService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly IMoneyService _moneyService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;

        public AfterProcessQaCheckFinalProcessEvent(
            IJobRepository repository,
            ITaskRepository taskRepository,
            IEventBus eventBus,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IJobService jobService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IMoneyService moneyService,
            IServiceProvider provider,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration, IMapper mapper)
        {
            _repository = repository;
            _taskRepository = taskRepository;
            _eventBus = eventBus;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _jobService = jobService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _moneyService = moneyService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _mapper = mapper;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
        }

        public async Task<Tuple<bool, string, string>> ProcessEvent(AfterProcessQaCheckFinalEvent evt, CancellationToken ct = default)
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
                    string methodName = "ProcessAfterProcessQaCheckFinal";
                    Log.Debug($"Start {methodName} - EventId: {evt.EventBusIntergrationEventId}");
                    var swTotal = Stopwatch.StartNew();
                    var sw = Stopwatch.StartNew();
                    string accessToken = evt.AccessToken;

                    await EnrichDataJob(evt);

                    var job = evt.Job;
                    var userInstanceId = job.UserInstanceId.GetValueOrDefault();
                    var jobEnds = new List<JobDto>();

                    if (string.IsNullOrEmpty(job.Value))
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(evt.Job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, evt.Job.WorkflowInstanceId.GetValueOrDefault(), null, "", null, accessToken: evt.AccessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(AfterProcessQaCheckFinalEvent)}: Error change current work flow step info for DocInstanceId:{evt.Job.DocInstanceId.GetValueOrDefault()} !");
                        }

                        Log.Error("ProcessQaCheckFinal value of job is null!");
                        return new Tuple<bool, string, string>(false, "ProcessQaCheckFinal value of job is null!", null);
                    }

                    var docItems = JsonConvert.DeserializeObject<List<DocItem>>(job.Value);
                    if (docItems == null || docItems.Count <= 0)
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(evt.Job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, evt.Job.WorkflowInstanceId.GetValueOrDefault(), null, "", null, accessToken: evt.AccessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(AfterProcessQaCheckFinalEvent)}: Error change current work flow step info for DocInstanceId:{evt.Job.DocInstanceId.GetValueOrDefault()} !");
                        }

                        Log.Error("ProcessQaCheckFinal value of job can not be parse!");
                        return new Tuple<bool, string, string>(false, "ProcessQaCheckFinal value of job can not be parse!", null);
                    }

                    var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                    var wfsInfoes = wfInfoes.Item1;
                    var wfSchemaInfoes = wfInfoes.Item2;

                    if (wfsInfoes == null || wfsInfoes.Count <= 0)
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(evt.Job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, evt.Job.WorkflowInstanceId.GetValueOrDefault(), null, "", null, accessToken: evt.AccessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(AfterProcessQaCheckFinalEvent)}: Error change current work flow step info for DocInstanceId:{evt.Job.DocInstanceId.GetValueOrDefault()} !");
                        }

                        Log.Error("ProcessCheckFinal can not get wfsInfoes!");
                        return new Tuple<bool, string, string>(false, "ProcessCheckFinal can not get wfsInfoes!", null);
                    }

                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);
                    var isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                    bool isConvergenceNextStep = isParallelStep;

                    // 1. Cập nhật thanh toán tiền cho worker & hệ thống// Cập nhật thanh toán tiền cho worker bước HIỆN TẠI (QACheckFinal)
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
                        //    CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Xác nhận QA",
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
                            CorrelationMessage = string.Format(MsgTransactionTemplate.MsgJobInfoes, "Xác nhận QA",
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

                    Log.Logger.Information($"ProcessQaCheckFinal: {job.Code} with DocInstanceId: {job.DocInstanceId} success!");

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

                    Log.Information($"ProcessQACheckFinal change step: DocInstanceId => {job.DocInstanceId}; ActionCode => {job.ActionCode}; WorkflowStepInstanceId => {job.WorkflowStepInstanceId}");
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
                            LastModificationDate = DateTime.UtcNow,
                            Status = (short)EnumEventBus.PublishMessageStatus.Nack,
                        };

                        try // try to publish event
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
                    var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                    var jObjConfigStep = JObject.Parse(crrWfsInfo.ConfigStep);
                    var jTokenConditionType = jObjConfigStep.GetValue(ConfigStepPropertyConstants.ConditionType);
                    var jTokenConfigStepConditions = jObjConfigStep.GetValue(ConfigStepPropertyConstants.ConfigStepConditions);

                    bool isProcessQAInBatchMode = true; // kiểm tra xem xử lý QA theo lô hay theo file đơn lẻ
                    List<InputParam> inputParamsForQARollback = null;
                    List<InputParam> inputParamsForQAPass = null;

                    if (nextWfsInfoes != null && nextWfsInfoes.Any())
                    {
                        bool isTriggerNextStep = false;
                        var isQAPass = job.QaStatus;

                        if (jTokenConditionType != null && jTokenConfigStepConditions != null)
                        {
                            bool isConditionTypeResult = Int16.TryParse(jTokenConditionType.ToString(), out var conditionType);
                            if (isConditionTypeResult)
                            {
                                switch (conditionType)
                                {
                                    case (short)EnumWorkflowStep.ConditionType.Default: // Điều kiện Đúng/Sai
                                        var configStepConditionDefaults =
                                            JsonConvert.DeserializeObject<List<ConfigStepConditionDefault>>(
                                                jTokenConfigStepConditions.ToString());
                                        if (configStepConditionDefaults != null && configStepConditionDefaults.Any())
                                        {
                                            var selectedConfigStepConditionDefault = configStepConditionDefaults.FirstOrDefault(x => x.Value == job.QaStatus);
                                            if (selectedConfigStepConditionDefault != null)
                                            {
                                                nextWfsInfoes = nextWfsInfoes.Where(x => x.InstanceId == selectedConfigStepConditionDefault.WorkflowStepInstanceId).ToList();

                                                // Sau bước QACheckFinal thì luôn luôn chỉ có 1 bước
                                                var nextWfsInfo = nextWfsInfoes.FirstOrDefault(x =>
                                                    x.InstanceId == selectedConfigStepConditionDefault.WorkflowStepInstanceId);

                                                /*
                                                 *  1. xét xem lô đã hoàn thiện hết chưa
                                                 *  1.1 Đã đến ngưỡng trả lại -> rollback những phiếu chưa pass
                                                 *  1.2 Nêu chưa đạt ngưỡng -> pass cả lô
                                                 *  2. Nếu lô chưa hoàn thiện -> Xử lý đơn lẻ 1 phiếu  
                                                 *  2.1 pass cho qua nextstep / false -> Không làm gì
                                                 */

                                                // Lấy danh sách jobs bước HIỆN TẠI (QACheckFinal) vòng hiện tại
                                                sw.Restart();

                                                var allJobsInBatchAndRound = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(),
                                                    crrWfsInfo.ActionCode, crrWfsInfo.InstanceId, null, job.DocPath, job.BatchJobInstanceId, job.NumOfRound);

                                                sw.Stop();
                                                Log.Debug($"{methodName} - EventId: {evt.EventBusIntergrationEventId} - allJobsInBatchAndRound-GetAllJobByWfs - Elasped time: {sw.ElapsedMilliseconds} ms");

                                                // Lấy danh sách jobs Complete bước TRƯỚC (CheckFinal)
                                                sw.Restart();

                                                var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                                                var preWfsInfo = prevWfsInfoes.FirstOrDefault();
                                                var completePrevJobs = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(), preWfsInfo?.ActionCode,
                                                        preWfsInfo?.InstanceId, (short)EnumJob.Status.Complete, job.DocPath,
                                                        job.BatchJobInstanceId, job.NumOfRound);

                                                sw.Stop();
                                                Log.Debug($"{methodName} - EventId: {evt.EventBusIntergrationEventId} - completePrevJobs-GetAllJobByWfs - Elasped time: {sw.ElapsedMilliseconds} ms");

                                                //int numOfFileInBatch = completePrevJobs.Count;
                                                var configQa = WorkflowHelper.GetConfigQa(crrWfsInfo.ConfigStep);

                                                var batchQASize = configQa.Item2; // Số phiếu / lô ; nếu = 0 thì cả thư mục là 1 lô
                                                var batchQASampling = configQa.Item3; // % lấy mẫu trong lô 
                                                var batchQAFalseThreshold = configQa.Item4; // ngưỡng sai: nếu >= % ngưỡng thì trả lại cả lô


                                                isProcessQAInBatchMode = configQa.Item1;

                                                //Tạm fix: nếu file upload không vào thư mục nào thì chạy theo chế độ QA đơn file
                                                if (string.IsNullOrEmpty(job.DocPath) || job.BatchJobInstanceId.GetValueOrDefault() == Guid.Empty)
                                                {
                                                    isProcessQAInBatchMode = false;
                                                }

                                                //xử lý QA theo single file hoặc là QA pass thì cho File đó qua luôn
                                                if (isProcessQAInBatchMode == false)
                                                {
                                                    isQAPass = job.QaStatus;

                                                    //lấy bước theo nhánh false
                                                    var stepCondition_by_case = configStepConditionDefaults.FirstOrDefault(x => x.Value == isQAPass);
                                                    nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                                                    var nextWfsInfo_case = nextWfsInfoes.SingleOrDefault(x => x.InstanceId == stepCondition_by_case.WorkflowStepInstanceId);

                                                    completePrevJobs = await _repository.GetAllJobByWfs(job.ProjectInstanceId.GetValueOrDefault(), preWfsInfo?.ActionCode,
                                                        preWfsInfo?.InstanceId, (short)EnumJob.Status.Complete, job.DocPath,
                                                        null, job.NumOfRound, job.DocInstanceId);

                                                    if (isQAPass == false) //rollback step (CheckFinal) cho phiếu hiện tại
                                                    {
                                                        inputParamsForQARollback = CreateInputParamForNextJob(completePrevJobs, wfsInfoes, wfSchemaInfoes, nextWfsInfo_case);
                                                        inputParamsForQARollback = AsignNote_Round_Value(inputParamsForQARollback, allJobsInBatchAndRound, isQAPass);
                                                    }
                                                    else if (isQAPass == true) // forward step (SyntheticData cho phiếu hiện tại
                                                    {
                                                        inputParamsForQAPass = CreateInputParamForNextJob(completePrevJobs, wfsInfoes, wfSchemaInfoes, nextWfsInfo_case);
                                                        inputParamsForQAPass = AsignNote_Round_Value(inputParamsForQAPass, allJobsInBatchAndRound, isQAPass);
                                                        inputParamsForQAPass.ForEach(x =>
                                                        {
                                                            x.Note = string.Empty;
                                                            x.QaStatus = isQAPass;
                                                        });
                                                    }

                                                    isTriggerNextStep = true;


                                                }
                                                else //xử lý QA theo Batch file và QAStatus=false;
                                                {
                                                    // 1. Kiểm tra điều kiện rollback khi QA False hoặc toàn bộ đã hoàn thành
                                                    if (job.QaStatus == false || allJobsInBatchAndRound.All(x => x.Status == (short)EnumJob.Status.Complete))
                                                    {

                                                        batchQASize = completePrevJobs.Count();
                                                        int numOfWrongQaCheck = Convert.ToInt32(Math.Round((decimal)batchQASize * (decimal)batchQAFalseThreshold / (decimal)100, MidpointRounding.ToEven));    // Số Phiếu trong Lô * PercentWrongQaCheck * 100%
                                                        if (numOfWrongQaCheck <= 0)
                                                        {
                                                            numOfWrongQaCheck = 1;
                                                        }
                                                        var completeQAFailJobs = allJobsInBatchAndRound.Where(x => x.Status == (short)EnumJob.Status.Complete && x.QaStatus == false).ToList();


                                                        // 1.1 Trả lại cả LÔ: nếu Total_QA_Fail >= numOfWrongQaCheck và hủy bỏ các job QA đang chờ xử lý
                                                        if (completeQAFailJobs.Count >= numOfWrongQaCheck)
                                                        {
                                                            // QA Fail =>
                                                            isQAPass = false;

                                                            // Lấy danh sách jobs CheckFinal Complete vòng hiện tại
                                                            var completeQAPassJobs = allJobsInBatchAndRound.Where(x => x.Status == (short)EnumJob.Status.Complete && x.QaStatus == true).ToList();

                                                            //Todo: loại bỏ đi những phiếu đã pass QA và ĐÃ được tạo job tiếp theo
                                                            completePrevJobs = completePrevJobs.Where(x => !completeQAPassJobs.Any(y => y.DocInstanceId == x.DocInstanceId)).ToList();

                                                            //lấy bước theo nhánh false
                                                            var stepCondition_false_case = configStepConditionDefaults.FirstOrDefault(x => x.Value == isQAPass);
                                                            nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                                                            var nextWfsInfo_false = nextWfsInfoes.SingleOrDefault(x => x.InstanceId == stepCondition_false_case.WorkflowStepInstanceId);

                                                            //Tạo ra lô phiếu rollback cho vòng mới
                                                            inputParamsForQARollback = CreateInputParamForNextJob(completePrevJobs, wfsInfoes, wfSchemaInfoes, nextWfsInfo_false);

                                                            //gắn note = QA_job.note và tăng Round
                                                            inputParamsForQARollback = AsignNote_Round_Value(inputParamsForQARollback, allJobsInBatchAndRound, isQAPass);


                                                            // Hủy bỏ các jobQA chờ xử lý Các jobs QA chưa complete còn lại chuyển trạng thái Ignore + cập nhật ReasonIgnore
                                                            var ignoreQaJobs = allJobsInBatchAndRound.Where(x => x.Status != (short)EnumJob.Status.Complete).ToList();
                                                            if (ignoreQaJobs != null && ignoreQaJobs.Any())
                                                            {
                                                                ignoreQaJobs.ForEach(x =>
                                                                {
                                                                    x.Status = (short)EnumJob.Status.Ignore;
                                                                    x.ReasonIgnore = "Job QA bị hủy bỏ do lô phiếu đã bị trả lại";
                                                                });
                                                                await _repository.UpdateMultiAsync(ignoreQaJobs);

                                                                //gửi update trạng thái cho Job Distribution Service để đồng bộ
                                                                await _jobService.PublishLogJobEvent(ignoreQaJobs, accessToken);
                                                            }

                                                            isTriggerNextStep = true;
                                                        }
                                                        else // 1.2 pass cả lô nếu lô đã hoàn thành xong
                                                        {
                                                            if (allJobsInBatchAndRound.All(x => x.Status == (short)EnumJob.Status.Complete))
                                                            {
                                                                isQAPass = true;
                                                                //lấy bước theo nhánh true
                                                                var stepCondition_pass_case = configStepConditionDefaults.FirstOrDefault(x => x.Value == isQAPass);
                                                                nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                                                                var nextWfsInfo_pass = nextWfsInfoes.SingleOrDefault(x => x.InstanceId == stepCondition_pass_case.WorkflowStepInstanceId);

                                                                inputParamsForQAPass = CreateInputParamForNextJob(completePrevJobs, wfsInfoes, wfSchemaInfoes, nextWfsInfo_pass);
                                                                inputParamsForQAPass = AsignNote_Round_Value(inputParamsForQAPass, allJobsInBatchAndRound, isQAPass);
                                                                inputParamsForQAPass.ForEach(x =>
                                                                {
                                                                    x.Note = string.Empty;
                                                                    x.QaStatus = isQAPass;
                                                                });
                                                                isTriggerNextStep = true;
                                                            }
                                                            else // lô chưa hoàn thành QA
                                                            {
                                                                // do nothing here
                                                            }
                                                        }
                                                    }
                                                    else // 2. nếu lô QA chưa hoàn thành hoặc Job.QAStatus=true -> xử lý đơn lẻ phiếu: Pass ->next step / False :do nothing
                                                    {
                                                        if (job.QaStatus == true)
                                                        {
                                                            isQAPass = true;
                                                            var currentJob = await _repository.GetByIdAsync(ObjectId.Parse(job.Id));
                                                            inputParamsForQAPass = CreateInputParamForNextJob(new List<Job>() { currentJob }, wfsInfoes, wfSchemaInfoes, nextWfsInfo);
                                                            inputParamsForQAPass.ForEach(x =>
                                                            {
                                                                x.Note = string.Empty;
                                                                x.QaStatus = isQAPass;
                                                            });
                                                            isTriggerNextStep = true;
                                                        }
                                                        else
                                                        {
                                                            //do nothing here
                                                        }
                                                    }
                                                }

                                                // Cập nhật trạng thái QAStatus cho các bước TRƯỚC (CheckFinal)
                                                sw.Restart();
                                                await UpdatePrevJobsQaStatus(completePrevJobs, job.QaStatus.GetValueOrDefault(), job.NumOfRound);
                                                sw.Stop();
                                                Log.Debug($"{methodName} - UpdatePrevJobsQaStatus - Elapsed time: {sw.ElapsedMilliseconds} ms");
                                            }
                                        }
                                        break;
                                    case (short)EnumWorkflowStep.ConditionType.FileExt: // Điều kiện theo định dạng file
                                        break;
                                    case (short)EnumWorkflowStep.ConditionType.DigitizedTemplate: // Điều kiện theo mẫu
                                        break;
                                }
                            }
                        }

                        if (isTriggerNextStep)
                        {

                            var parallelJobInstanceId = Guid.NewGuid();

                            //đằng sau QA step -> Chỉ đến CheckFinal (nếu False) hoặc SyntheticData (nếu True)

                            //Lựa chọn các nhánh cần tạo job tùy theo điều kiện isQAPass
                            var configStepConditionDefaults = JsonConvert.DeserializeObject<List<ConfigStepConditionDefault>>(jTokenConfigStepConditions.ToString());
                            var stepCondition = configStepConditionDefaults.FirstOrDefault(x => x.Value == isQAPass);
                            nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                            nextWfsInfoes = nextWfsInfoes.Where(x => x.InstanceId == stepCondition.WorkflowStepInstanceId).ToList();

                            WorkflowStepInfo nextWfsInfo = null;

                            //xử lý đặc biệt: trigger đơn lẻ cho file Pass QA
                            if (job.QaStatus == true && isQAPass == false)
                            {
                                stepCondition = configStepConditionDefaults.FirstOrDefault(x => x.Value == true);
                                nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                                nextWfsInfoes = nextWfsInfoes.Where(x => x.InstanceId == stepCondition.WorkflowStepInstanceId).ToList();
                                nextWfsInfo = nextWfsInfoes.FirstOrDefault();

                                await ProcessTriggerNextStep(job, docItems, parallelJobInstanceId, isConvergenceNextStep, null, nextWfsInfo, wfsInfoes, wfSchemaInfoes, accessToken);
                            }

                            //xý lý case thông thường trigger theo điều kiện isQAPass
                            stepCondition = configStepConditionDefaults.FirstOrDefault(x => x.Value == isQAPass);
                            nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                            nextWfsInfoes = nextWfsInfoes.Where(x => x.InstanceId == stepCondition.WorkflowStepInstanceId).ToList();
                            nextWfsInfo = nextWfsInfoes.FirstOrDefault();

                            List<InputParam> inputParams = null;
                            if (isQAPass == false)
                            {
                                inputParams = inputParamsForQARollback;
                            }
                            else if (isQAPass == true)
                            {
                                inputParams = inputParamsForQAPass;
                            }

                            await ProcessTriggerNextStep(job, docItems, parallelJobInstanceId, isConvergenceNextStep, inputParams, nextWfsInfo, wfsInfoes, wfSchemaInfoes, accessToken);
                            
                            // Update current wfs status is complete
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(AfterProcessQaCheckFinalEvent)}: Error change current work flow step info for DocInstanceId:{job.DocInstanceId.GetValueOrDefault()} !");
                            }
                        }
                    }

                    swTotal.Stop();
                    Log.Debug($"End {methodName} - EventId: {evt.EventBusIntergrationEventId} - Total Elapsed time {swTotal.ElapsedMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    // Update current wfs status is error
                    var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(evt.Job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, evt.Job.WorkflowInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: evt.AccessToken);
                    if (!resultDocChangeCurrentWfsInfo.Success)
                    {
                        Log.Logger.Error($"{nameof(AfterProcessQaCheckFinalEvent)}: Error change current work flow step info for DocInstanceId:{evt.Job.DocInstanceId.GetValueOrDefault()} !");
                    }
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

        private async Task UpdatePrevJobsQaStatus(List<Job> prevJobs, bool qaStatus, short numOfRound)
        {
            if (prevJobs.Count == 0)
            {
                return;
            }

            prevJobs = prevJobs.Where(x => x.NumOfRound == numOfRound).ToList();
            prevJobs.ForEach(x =>
            {
                x.QaStatus = qaStatus;
                x.RightStatus = !qaStatus ? (short)EnumJob.RightStatus.Wrong : (short)EnumJob.RightStatus.Confirmed;
            });
            await _repository.UpdateMultiAsync(prevJobs);
        }

        private async Task ProcessTriggerNextStep(JobDto job, List<DocItem> docItems, Guid parallelJobInstanceId, bool isConvergenceNextStep, List<InputParam> inputParams, WorkflowStepInfo nextWfsInfo, List<WorkflowStepInfo> wfsInfoes, List<WorkflowSchemaConditionInfo> wfSchemaInfoes, string accessToken)
        {
            if (nextWfsInfo.ActionCode != ActionCodeConstants.End)
            {
                var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
            ConfigStepPropertyConstants.IsPaidStep);
                var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                decimal price;
                string value;

                value = JsonConvert.SerializeObject(docItems);
                price = isPaid
                    ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                        job.DigitizedTemplateInstanceId)
                    : 0;

                var output = new InputParam
                {
                    FileInstanceId = job.FileInstanceId,
                    ActionCode = nextWfsInfo.ActionCode,
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
                    //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                    //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                    Value = value,
                    OldValue = value,
                    Price = price,
                    ClientTollRatio = job.ClientTollRatio,
                    WorkerTollRatio = job.WorkerTollRatio,
                    IsDivergenceStep = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File,
                    ParallelJobInstanceId =
                        nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                            ? parallelJobInstanceId
                            : null,
                    IsConvergenceNextStep =
                        nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                        isConvergenceNextStep,
                    Note = job.Note,
                    NumOfRound = job.QaStatus == false ? (short)(job.NumOfRound + 1) : job.NumOfRound,
                    BatchName = job.BatchName,
                    BatchJobInstanceId = job.BatchJobInstanceId,
                    QaStatus = job.QaStatus,
                    TenantId = job.TenantId,
                };
                inputParams.ForEach(x => x.Price = price);

                output.InputParams = inputParams;

                var taskEvt = new TaskEvent
                {
                    Input = JsonConvert.SerializeObject(output),     // output của bước trước là input của bước sau
                    AccessToken = accessToken
                };

                await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);

                Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {job.DocInstanceId}, JobCode: {job.Code}");
            }
            else
            {
                await _moneyService.ChargeMoneyForCompleteDoc(wfsInfoes, wfSchemaInfoes, docItems, job.DocInstanceId.GetValueOrDefault(), accessToken);
            }
        }

        private List<InputParam> CreateInputParamForNextJob(List<Model.Entities.Job> listJob, List<WorkflowStepInfo> wfsInfoes, List<WorkflowSchemaConditionInfo> wfSchemaInfoes, WorkflowStepInfo nextWfsInfo)
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
                WorkflowStepInstanceId = nextWfsInfo.InstanceId,//j.WorkflowStepInstanceId,
                //WorkflowStepInstanceIds = null,
                //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                Value = j.Value,
                OldValue = j.Value,
                Price = j.Price,
                ClientTollRatio = j.ClientTollRatio,
                WorkerTollRatio = j.WorkerTollRatio,
                IsDivergenceStep = false,
                ParallelJobInstanceId = j.ParallelJobInstanceId,
                IsConvergenceNextStep = nextWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File,
                NumOfRound = (short)(j.NumOfRound),
                BatchName = j.BatchName,
                BatchJobInstanceId = j.BatchJobInstanceId,
                Note = j.Note,
                TenantId = j.TenantId,
                LastModifiedBy = j.LastModifiedBy
            }).ToList();

            return result;
        }

        /// <summary>
        /// Gắn lại giá trị Value / Note / Round từ QA Job cho CheckFinal hoăc SyntheticData Job
        /// </summary>
        /// <param name="jobsForNextStep"></param>
        /// <param name="qaJobs"></param>
        /// <param name="isQAPass"></param>
        /// <returns></returns>
        private List<InputParam> AsignNote_Round_Value(List<InputParam> jobsForNextStep, List<Job> qaJobs, bool? isQAPass)
        {
            qaJobs = qaJobs.OrderByDescending(o => o.NumOfRound).ThenByDescending(o => o.LastModificationDate).ToList();
            foreach (var jobNextStepItem in jobsForNextStep)
            {
                var qaItem = qaJobs.FirstOrDefault(x => x.DocInstanceId == jobNextStepItem.DocInstanceId);
                if (qaItem != null)
                {
                    jobNextStepItem.Note = qaItem.Note;
                    jobNextStepItem.Value = qaItem.Value;
                    jobNextStepItem.OldValue = qaItem.Value;
                }

                if (string.IsNullOrEmpty(jobNextStepItem.Note) && isQAPass == false)
                {
                    jobNextStepItem.Note = "Dữ liệu thuộc lô bị QA trả lại";
                }

                if (isQAPass == false)
                {
                    jobNextStepItem.NumOfRound += 1;
                }
            }

            return jobsForNextStep;
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

        private async Task EnrichDataJob(AfterProcessQaCheckFinalEvent evt)
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
