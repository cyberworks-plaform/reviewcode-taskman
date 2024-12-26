using AutoMapper;
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
using Azure.Core;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Common.Lib.Helpers;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.EventBusRabbitMq.Lib.Interfaces;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
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
    public interface ITaskProcessEvent : IBaseInboxProcessEvent<TaskEvent>, IDisposable { }

    public class TaskProcessEvent : Disposable, ITaskProcessEvent
    {
        private readonly IEventBus _eventBus;
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly IMapper _mapper;
        private readonly IJobRepository _jobRepository;
        private readonly ISequenceJobRepository _sequenceJobRepository;
        private readonly ITaskRepository _taskRepository;
        private readonly IQueueLockRepository _queueLockRepository;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IDocTypeFieldClientService _docTypeFieldClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;

        private bool _isCreateSingleJob;
        private bool _isCreateMultiJobFile;

        private bool _isParallelStep;
        private int _numOfResourceInJob;
        private bool _isCrrStepRequiredAllBeforeStepComplete;

        private const int TimeOut = 600;   // Default HttpClient timeout is 100s

        public TaskProcessEvent(IEventBus eventBus,
            IBaseHttpClientFactory clientFatory,
            IJobRepository jobRepository,
            ISequenceJobRepository sequenceJobRepository,
            ITaskRepository taskRepository,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IServiceProvider provider,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IQueueLockRepository queueLockRepository,
            IMapper mapper,
            IDocTypeFieldClientService docTypeFieldClientService,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration)
        {
            _eventBus = eventBus;
            _clientFatory = clientFatory;
            _jobRepository = jobRepository;
            _sequenceJobRepository = sequenceJobRepository;
            _taskRepository = taskRepository;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _userProjectClientService = userProjectClientService;
            _transactionClientService = transactionClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _queueLockRepository = queueLockRepository;
            _mapper = mapper;
            _docTypeFieldClientService = docTypeFieldClientService;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
            _cachingHelper = provider.GetService<ICachingHelper>();
            _useCache = _cachingHelper != null;
        }

        public async Task<Tuple<bool, string, string>> ProcessEvent(TaskEvent @event, CancellationToken ct = default)
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
                    var inputParam = JsonConvert.DeserializeObject<InputParam>(@event.Input);
                    if (inputParam == null)
                    {
                        Log.Logger.Error("inputParam is null!");
                        return new Tuple<bool, string, string>(true, "inputParam is null!", null);
                    }

                    Stopwatch sw = Stopwatch.StartNew();
                    Log.Information($"Start handle TaskEvent Id: {@event.EventBusIntergrationEventId.ToString()} - ActionCode: {inputParam.ActionCode} - DocId: {inputParam.DocInstanceId.GetValueOrDefault().ToString()}");

                    var isEnrichData = await EnrichData(inputParam, accessToken);
                    if (isEnrichData)
                    {
                        @event.Input = JsonConvert.SerializeObject(inputParam);
                    }

                    var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                    if (wfsInfoes == null)
                    {
                        Log.Logger.Error("wfsInfoes is null!");
                        return new Tuple<bool, string, string>(false, "wfsInfoes is null!", null);
                    }

                    var wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);
                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                    var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                    var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                    //var siblingWfsInfoes = WorkflowHelper.GetSiblingSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());

                    bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                    var parallelJobInstanceId = Guid.NewGuid();

                    _isCreateSingleJob = crrWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File;
                    _isCreateMultiJobFile = inputParam.InputParams != null && inputParam.InputParams.Any();

                    _isCrrStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);

                    _numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(crrWfsInfo.ConfigStep);

                    _isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);

                    if (inputParam.IsFromQueueLock)
                    {
                        Log.Logger.Information(
                            $"Start handle integration event from {nameof(TaskIntegrationEventHandler)}: step {inputParam.ActionCode}, WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} with DocInstanceId: {inputParam.DocInstanceId}, IsFromQueueLock: true");
                    }
                    else
                    {
                        Log.Logger.Information(
                            $"Start handle integration event from {nameof(TaskIntegrationEventHandler)}: step {inputParam.ActionCode}, WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                    }

                    // Kiểm tra doc có đang tồn tại hay đã xóa => Nếu đã xóa thì clear message
                    var checkDocExisted = await _docClientService.GetByInstanceIdAsync(inputParam.DocInstanceId.GetValueOrDefault(), accessToken);
                    if (checkDocExisted == null || !checkDocExisted.Success)
                    {
                        Log.Error("Error calling DocClientService to check doc existed");
                        return new Tuple<bool, string, string>(false,
                            "Error calling DocClientService to check doc existed", null);
                    }
                    if (checkDocExisted.Data == null || checkDocExisted.Data.IsActive == false)
                    {
                        Log.Information($"Doc is deleted before handling message. Message will be ignored. EventId: {@event.EventBusIntergrationEventId} DocId: {inputParam.DocInstanceId}");
                        return new Tuple<bool, string, string>(true,
                            $"Doc is deleted before handling message. Message will be ignored. EventId: {@event.EventBusIntergrationEventId} DocId: {inputParam.DocInstanceId}",
                            null);
                    }

                    if (crrWfsInfo.ActionCode == ActionCodeConstants.Condition)
                    {
                        // Sau bước Condition luôn luôn chỉ có 1 bước => IsMultipleNextStep = false
                        // Sau bước Condition ko tồn tại bước HỘI TỤ => IsConvergenceNextStep = false

                        var jObjConfigStep = JObject.Parse(crrWfsInfo.ConfigStep);
                        var jTokenConditionType = jObjConfigStep.GetValue(ConfigStepPropertyConstants.ConditionType);
                        var jTokenConfigStepConditions = jObjConfigStep.GetValue(ConfigStepPropertyConstants.ConfigStepConditions);
                        if (jTokenConditionType != null && jTokenConfigStepConditions != null)
                        {
                            bool isConditionTypeResult = Int16.TryParse(jTokenConditionType.ToString(), out var conditionType);
                            if (isConditionTypeResult)
                            {
                                switch (conditionType)
                                {
                                    case (short)EnumWorkflowStep.ConditionType.Default:     // Điều kiện Đúng/Sai
                                        if (prevWfsInfoes == null || !prevWfsInfoes.Any())
                                        {
                                            break;
                                        }

                                        var configStepConditionDefaults =
                                            JsonConvert.DeserializeObject<List<ConfigStepConditionDefault>>(
                                                jTokenConfigStepConditions.ToString());
                                        var prevWfsInfo = prevWfsInfoes.First();    // Dựa vào prevWfsInfo để xác định xem Điều kiện Đúng/Sai là theo File hay theo Meta

                                        // Điều kiện Đúng/Sai theo File
                                        if (prevWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                        {
                                            bool valueConditionDefault = true;
                                            if (!string.IsNullOrEmpty(inputParam.ConditionalValue))
                                            {
                                                valueConditionDefault = Boolean.Parse(inputParam.ConditionalValue);
                                            }

                                            if (configStepConditionDefaults != null && configStepConditionDefaults.Any())
                                            {
                                                var selectedConfigStepConditionDefault = configStepConditionDefaults.FirstOrDefault(x => x.Value == valueConditionDefault);
                                                if (selectedConfigStepConditionDefault != null)
                                                {
                                                    // Sau bước Condition thì luôn luôn chỉ có 1 bước
                                                    var nextWfsInfo = nextWfsInfoes.FirstOrDefault(x =>
                                                        x.InstanceId == selectedConfigStepConditionDefault.WorkflowStepInstanceId);

                                                    // Trigger bước tiếp theo
                                                    if (nextWfsInfo != null && nextWfsInfo.ActionCode != ActionCodeConstants.End)
                                                    {
                                                        int numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(nextWfsInfo.ConfigStep);
                                                        bool isDivergenceStep = isMultipleNextStep || numOfResourceInJob > 1;

                                                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                                                            ConfigStepPropertyConstants.IsPaidStep);
                                                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                                                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                                                        // Tổng hợp price cho các bước TIẾP THEO
                                                        decimal price = isPaid
                                                            ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                                                inputParam.DigitizedTemplateInstanceId)
                                                            : 0;
                                                        
                                                        var output = new InputParam
                                                        {
                                                            FileInstanceId = inputParam.FileInstanceId,
                                                            ActionCode = nextWfsInfo.ActionCode,
                                                            //ActionCodes = null,       // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                            DocInstanceId = inputParam.DocInstanceId,
                                                            DocName = inputParam.DocName,
                                                            DocPath = inputParam.DocPath,
                                                            DocCreatedDate = inputParam.DocCreatedDate,
                                                            TaskId = inputParam.TaskId,
                                                            TaskInstanceId = inputParam.TaskInstanceId,
                                                            ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                                            ProjectInstanceId = inputParam.ProjectInstanceId,
                                                            SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                                                            DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                                                            DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                                                            WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                                            WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                                                            //WorkflowStepInstanceIds = null,   // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                            //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                                                            //WorkflowSchemaInfoes = inputParam.WorkflowSchemaInfoes, // Không truyền thông tin này để giảm dung lượng msg
                                                            Value = inputParam.Value,
                                                            Price = price,
                                                            //WorkflowStepPrices = null,    // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                            ClientTollRatio = inputParam.ClientTollRatio,
                                                            WorkerTollRatio = inputParam.WorkerTollRatio,
                                                            IsDivergenceStep = prevWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                                               isDivergenceStep,
                                                            ParallelJobInstanceId =
                                                                nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                                                    ? parallelJobInstanceId
                                                                    : null,
                                                            IsConvergenceNextStep = false,              // Sau bước ĐIỀU KIỆN không tồn tại bước HỘI TỤ
                                                            TenantId = inputParam.TenantId,
                                                            ItemInputParams = inputParam.ItemInputParams
                                                        };
                                                        var taskEvt = new TaskEvent
                                                        {
                                                            Input = JsonConvert.SerializeObject(output),
                                                            AccessToken = accessToken
                                                        };
                                                        await TriggerNextStep(taskEvt, nextWfsInfo.ActionCode);

                                                        Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                                                    }
                                                }
                                            }
                                        }
                                        else if (prevWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)   // Điều kiện Đúng/Sai theo Meta
                                        {
                                            var lstDocItemFull = new List<DocItem>();
                                            var lstDocItemFullRs = await _docClientService.GetDocItemByDocInstanceId(inputParam.DocInstanceId.GetValueOrDefault(), accessToken);
                                            if (lstDocItemFullRs.Success && lstDocItemFullRs.Data != null)
                                            {
                                                lstDocItemFull = lstDocItemFullRs.Data;
                                            }

                                            foreach (var itemInput in inputParam.ItemInputParams)
                                            {
                                                var valueConditionDefaultItem = true;
                                                if (!string.IsNullOrEmpty(itemInput.ConditionalValue))
                                                {
                                                    Boolean.TryParse(itemInput.ConditionalValue, out valueConditionDefaultItem);
                                                }

                                                if (configStepConditionDefaults != null && configStepConditionDefaults.Any())
                                                {
                                                    var selectedConfigStepConditionDefaultItem = configStepConditionDefaults.FirstOrDefault(x => x.Value == valueConditionDefaultItem);
                                                    if (selectedConfigStepConditionDefaultItem != null)
                                                    {
                                                        // Sau bước Condition thì luôn luôn chỉ có 1 bước
                                                        var nextWfsInfo = nextWfsInfoes.FirstOrDefault(x =>
                                                            x.InstanceId == selectedConfigStepConditionDefaultItem.WorkflowStepInstanceId);

                                                        int numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(nextWfsInfo.ConfigStep);
                                                        bool isDivergenceStep = isMultipleNextStep || numOfResourceInJob > 1;

                                                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                                                            ConfigStepPropertyConstants.IsPaidStep);
                                                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                                                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                                                        // Trigger bước tiếp theo
                                                        if (nextWfsInfo != null && nextWfsInfo.ActionCode != ActionCodeConstants.End)
                                                        {
                                                            var isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                                                            decimal price = 0;
                                                            string value = null;
                                                            var prevOfNextWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                                                            var prevOfNextWfsInstanceIds = prevOfNextWfsInfoes.Select(x => x.InstanceId).ToList();
                                                            var prevOfNextWfsJobs = await _jobRepository.GetJobByWfsInstanceIds(inputParam.DocInstanceId.GetValueOrDefault(), prevOfNextWfsInstanceIds);
                                                            prevOfNextWfsJobs = prevOfNextWfsJobs.Where(x => x.RightStatus == (short)EnumJob.RightStatus.Correct).ToList();   // Chỉ lấy các jobs có trạng thái Đúng
                                                            if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                            {
                                                                // Tổng hợp các thông số cho các bước TIẾP THEO
                                                                itemInput.IsDivergenceStep = isDivergenceStep;
                                                                itemInput.ParallelJobInstanceId = parallelJobInstanceId;
                                                                itemInput.IsConvergenceNextStep = false;        // Sau bước ĐIỀU KIỆN không tồn tại bước HỘI TỤ

                                                                // Tổng hợp price cho các bước TIẾP THEO
                                                                itemInput.Price = isPaid
                                                                    ? MoneyHelper.GetPriceByConfigPriceV2(
                                                                        nextWfsInfo.ConfigPrice,
                                                                        inputParam.DigitizedTemplateInstanceId,
                                                                        itemInput.DocTypeFieldInstanceId)
                                                                    : 0;
                                                            }
                                                            else
                                                            {
                                                                if (prevOfNextWfsJobs != null && prevOfNextWfsJobs.Any() &&
                                                                    prevOfNextWfsJobs.All(x =>
                                                                        x.Status == (short)EnumJob.Status.Complete ||
                                                                        x.Status == (short)EnumJob.Status.Ignore))
                                                                {
                                                                    price = isPaid
                                                                        ? MoneyHelper.GetPriceByConfigPriceV2(
                                                                            nextWfsInfo.ConfigPrice,
                                                                            inputParam.DigitizedTemplateInstanceId)
                                                                        : 0;

                                                                    //  Chỉ lấy các jobs có trạng thái RightStatus là Correct
                                                                    prevOfNextWfsJobs = prevOfNextWfsJobs.Where(x =>
                                                                            x.RightStatus ==
                                                                            (short)EnumJob.RightStatus.Correct)
                                                                        .ToList();
                                                                    var docItems = prevOfNextWfsJobs.Select(x => new DocItem
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
                                                                        PrivateCategoryInstanceId =
                                                                            x.PrivateCategoryInstanceId,
                                                                        IsMultipleSelection = x.IsMultipleSelection,
                                                                        CoordinateArea = x.CoordinateArea,
                                                                        //DocFieldValueId = 0,
                                                                        DocFieldValueInstanceId = x.DocFieldValueInstanceId,
                                                                        Value = x.Value
                                                                    }).ToList();

                                                                    if (isNextStepRequiredAllBeforeStepComplete)
                                                                    {
                                                                        if (lstDocItemFull != null && lstDocItemFull.Any())
                                                                        {
                                                                            var existDocTypeFieldInstanceId =
                                                                                prevOfNextWfsJobs.Select(x =>
                                                                                    x.DocTypeFieldInstanceId).ToList();
                                                                            var missDocItem = lstDocItemFull.Where(x =>
                                                                                !existDocTypeFieldInstanceId.Contains(
                                                                                    x.DocTypeFieldInstanceId)).ToList();
                                                                            if (missDocItem != null &&
                                                                                missDocItem.Count > 0)
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
                                                                FileInstanceId = inputParam.FileInstanceId,
                                                                ActionCode = nextWfsInfo.ActionCode,
                                                                //ActionCodes = null,       // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                                DocInstanceId = inputParam.DocInstanceId,
                                                                DocName = inputParam.DocName,
                                                                DocPath = inputParam.DocPath,
                                                                DocCreatedDate = inputParam.DocCreatedDate,
                                                                TaskId = inputParam.TaskId,
                                                                TaskInstanceId = inputParam.TaskInstanceId,
                                                                ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                                                ProjectInstanceId = inputParam.ProjectInstanceId,
                                                                SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                                                                DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                                                                DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                                                                WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                                                WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                                                                //WorkflowStepInstanceIds = null,   // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                                //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                                                                //WorkflowSchemaInfoes = inputParam.WorkflowSchemaInfoes, // Không truyền thông tin này để giảm dung lượng msg
                                                                Value = value,
                                                                Price = price,
                                                                //WorkflowStepPrices = null,    // Sau bước ĐIỀU KIỆN không tồn tại MultipleNextStep
                                                                ClientTollRatio = inputParam.ClientTollRatio,
                                                                WorkerTollRatio = inputParam.WorkerTollRatio,
                                                                //IsDivergenceStep = false,
                                                                //ParallelJobInstanceIdNextStep = null,
                                                                //IsConvergenceNextStep = false,              // Sau bước ĐIỀU KIỆN không tồn tại bước HỘI TỤ
                                                                TenantId = inputParam.TenantId,
                                                                ItemInputParams = inputParam.ItemInputParams
                                                            };
                                                            var taskEvt = new TaskEvent
                                                            {
                                                                Input = JsonConvert.SerializeObject(output),
                                                                AccessToken = accessToken
                                                            };

                                                            bool isTriggerNextStep = false;
                                                            if (isNextStepRequiredAllBeforeStepComplete)
                                                            {
                                                                // Nếu bước HIỆN TẠI yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
                                                                var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                                                                // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                                                                var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                                                                if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                                                                {
                                                                    Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                                                                    return new Tuple<bool, string, string>(false,
                                                                        "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                                                        null);
                                                                }

                                                                var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                                                                var hasJobWaitingOrProcessing = await _jobRepository.CheckHasJobWaitingOrProcessingByMultiWfs(inputParam.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                                                                if (!hasJobWaitingOrProcessing)
                                                                {
                                                                    var countOfExpectJobs = lstDocItemFull.Count(x => !string.IsNullOrEmpty(x.CoordinateArea));
                                                                    if (prevOfNextWfsJobs.Count == countOfExpectJobs) // Số lượng prevOfNextWfsJobs = countOfExpectJobs thì mới next step
                                                                    {
                                                                        // Xét trường hợp tất cả prevJobs cùng done tại 1 thời điểm
                                                                        bool triggerNextStepHappend = await TriggerNextStepHappened(
                                                                            inputParam.DocInstanceId.GetValueOrDefault(),
                                                                            inputParam.WorkflowStepInstanceId.GetValueOrDefault());
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
                                                                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                                                if (!resultDocChangeCurrentWfsInfo.Success)
                                                                {
                                                                    Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId {inputParam.DocInstanceId.GetValueOrDefault()} !");
                                                                }
                                                                Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }

                                        break;
                                    case (short)EnumWorkflowStep.ConditionType.FileExt:     // Điều kiện theo định dạng file
                                        var configStepConditionFileExts =
                                            JsonConvert.DeserializeObject<List<ConfigStepConditionFileExt>>(
                                                jTokenConfigStepConditions.ToString());

                                        break;
                                    case (short)EnumWorkflowStep.ConditionType.DigitizedTemplate:   // Điều kiện theo mẫu
                                        Guid valueConditionDigitizedTemplate = Guid.Empty;
                                        if (!string.IsNullOrEmpty(inputParam.ConditionalValue))
                                        {
                                            Guid.TryParse(inputParam.ConditionalValue, out valueConditionDigitizedTemplate);
                                        }
                                        var configStepConditionDigitizedTemplates =
                                            JsonConvert.DeserializeObject<List<ConfigStepConditionDigitizedTemplate>>(
                                                jTokenConfigStepConditions.ToString());
                                        if (configStepConditionDigitizedTemplates != null && configStepConditionDigitizedTemplates.Any())
                                        {
                                            var selectedConfigStepConditionDigitizedTemplate = configStepConditionDigitizedTemplates.FirstOrDefault(x => x.DigitizedTemplateInstanceIds.Contains(valueConditionDigitizedTemplate));
                                            if (selectedConfigStepConditionDigitizedTemplate != null)
                                            {
                                                var nextWfsInfo = nextWfsInfoes.FirstOrDefault(x =>
                                                    x.InstanceId == selectedConfigStepConditionDigitizedTemplate.WorkflowStepInstanceId);
                                                if (nextWfsInfo != null)
                                                {
                                                    decimal priceJob = MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice, inputParam.DigitizedTemplateInstanceId);  // Giá tiền cho job ở bước tiếp theo

                                                    var output = new InputParam
                                                    {
                                                        FileInstanceId = inputParam.FileInstanceId,
                                                        ActionCode = nextWfsInfo.ActionCode,
                                                        //ActionCodes = null,
                                                        DocInstanceId = inputParam.DocInstanceId,
                                                        DocName = inputParam.DocName,
                                                        DocCreatedDate = inputParam.DocCreatedDate,
                                                        TaskId = inputParam.TaskId,
                                                        TaskInstanceId = inputParam.TaskInstanceId,
                                                        ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                                        ProjectInstanceId = inputParam.ProjectInstanceId,
                                                        SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                                                        DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                                                        DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                                                        WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                                        WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                                                        //WorkflowStepInstanceIds = null,
                                                        //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                                                        //WorkflowSchemaInfoes = inputParam.WorkflowSchemaInfoes, // Không truyền thông tin này để giảm dung lượng msg
                                                        Value = inputParam.Value,
                                                        Price = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File ? priceJob : 0,
                                                        ClientTollRatio = inputParam.ClientTollRatio,
                                                        WorkerTollRatio = inputParam.WorkerTollRatio,
                                                        //IsMultipleNextStep = false,
                                                        //ParallelJobInstanceId = null,
                                                        ItemInputParams = inputParam.ItemInputParams,
                                                        DocPath = inputParam.DocPath
                                                    };
                                                    var evt = new TaskEvent
                                                    {
                                                        Input = JsonConvert.SerializeObject(output),
                                                        AccessToken = accessToken
                                                    };
                                                    await TriggerNextStep(evt, nextWfsInfo.ActionCode);
                                                }
                                            }
                                        }

                                        break;
                                    case (short)EnumWorkflowStep.ConditionType.DocTypeField:        // Điều kiện theo Meta
                                        var configStepConditionDocTypeFields =
                                            JsonConvert.DeserializeObject<List<ConfigStepConditionDocTypeField>>(
                                                jTokenConfigStepConditions.ToString());

                                        break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // 0.1. Kiểm tra doc có đang khóa hay không
                        var checkLockDocRs = await _docClientService.CheckLockDoc(inputParam.DocInstanceId.GetValueOrDefault(), accessToken);
                        var isLocked = checkLockDocRs.Success && checkLockDocRs.Data;
                        if (isLocked)
                        {
                            await CreateQueueLockes(inputParam);
                            return new Tuple<bool, string, string>(true, null, null);
                        }

                        // 0.2. Kiểm tra xem có phải từ nguồn QueueLock hay ko, nếu đúng thì xóa queueLock đi
                        if (inputParam.IsFromQueueLock)
                        {
                            if (_isCreateSingleJob)
                            {
                                await _queueLockRepository.DeleteQueueLockCompleted(inputParam.DocInstanceId.GetValueOrDefault(),
                                    inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                            }
                            else if (inputParam.ItemInputParams != null && inputParam.ItemInputParams.Any())
                            {
                                foreach (var itemInput in inputParam.ItemInputParams)
                                {
                                    await _queueLockRepository.DeleteQueueLockCompleted(inputParam.DocInstanceId.GetValueOrDefault(),
                                        inputParam.WorkflowStepInstanceId.GetValueOrDefault(), itemInput.DocTypeFieldInstanceId,
                                        itemInput.DocFieldValueInstanceId);
                                }
                            }
                        }

                        // 1. Tạo jobs Waitting cho bước hiện tại; nếu job đã được tạo trước đó rồi thì sẽ không tạo lại-> tránh duplicate job
                        var jobsResponse = await CreateJobs(inputParam, crrWfsInfo.ConfigStep, accessToken);
                        var jobs = jobsResponse.Item2;
                        if (!@event.IsRetry && jobsResponse.Item1)
                        {
                            Log.Information($"Job is created before message come. This message will be ignored. DocID: {inputParam.DocInstanceId} - ActionCode: {inputParam.ActionCode}");
                            return new Tuple<bool, string, string>(true, $"Job is created before message come. This message will be ignored. DocID: {inputParam.DocInstanceId} - ActionCode: {inputParam.ActionCode}", null);
                        }
                        // Update current wfs status is waiting
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Waiting, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId {inputParam.DocInstanceId.GetValueOrDefault()} !");
                        }
                        // 1.0. Nếu job là manual thì gửi message sang DistributionJob
                        if (jobs.Any() && !crrWfsInfo.IsAuto)
                        {
                            var logJobEvt = new LogJobEvent
                            {
                                LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
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
                            try //try to publish event
                            {
                                _eventBus.Publish(logJobEvt, nameof(LogJobEvent).ToLower());

                            }
                            catch (Exception exPublishEvent)
                            {
                                Log.Error(exPublishEvent, "Error publish for event LogJobEvent ");

                                try // try to save event to DB for retry later
                                {
                                    await _outboxIntegrationEventRepository.AddAsync(outboxEntity);
                                }
                                catch (Exception exSaveDB)
                                {
                                    Log.Error(exSaveDB, "Error save DB for event LogJobEvent");
                                    //Do not throw exception for LogJobEvent
                                }
                            }

                        }

                        // 1.1. Check xem có phải bước HIỆN TẠI bị bỏ qua hay ko?
                        if (jobs.Any() && !_isCreateSingleJob)
                        {
                            // Tách jobs ra làm 2 list, 1 list ignore và 1 list theo luồng bình thường
                            var allJobs = jobs.ToList();
                            var ignoreJobs = allJobs.Where(x => x.Status == (short)EnumJob.Status.Ignore).ToList();
                            if (ignoreJobs.Any())
                            {
                                // Tách ItemInputParams ra làm 2 list tương ứng
                                var ignoreItemInputParams = new List<ItemInputParam>();
                                var normalItemInputParams = new List<ItemInputParam>();
                                var ignoreParallelJobInstanceId = Guid.NewGuid();
                                foreach (var ignoreJob in ignoreJobs)
                                {
                                    // inputParam => old
                                    // ignoreItemInputParam => new
                                    var ignoreItemInputParam = inputParam.ItemInputParams.FirstOrDefault(x =>
                                        x.DocTypeFieldInstanceId == ignoreJob.DocTypeFieldInstanceId &&
                                        x.DocFieldValueInstanceId == ignoreJob.DocFieldValueInstanceId);
                                    if (ignoreItemInputParam != null)
                                    {
                                        //bool ignoreIsMultipleNextStep = nextWfsInfoes.Count > 1;    // always false
                                        var ignoreIsParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                                        bool ignoreIsConvergenceNextStep = ignoreIsParallelStep;
                                        int ignoreNumOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(crrWfsInfo.ConfigStep);
                                        bool ignoreIsDivergenceStep = ignoreNumOfResourceInJob > 1;
                                        var prevIgnoreJobInfos = ignoreJobs
                                            .Where(x => x.DocTypeFieldInstanceId == ignoreJob.DocTypeFieldInstanceId &&
                                                        x.DocFieldValueInstanceId == ignoreJob.DocFieldValueInstanceId)
                                            .Select(x => new PrevJobInfo
                                            {
                                                Id = x.Id.ToString(),
                                                UserInstanceId = null,
                                                WorkflowStepInstanceId =
                                                    inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                ActionCode = inputParam.ActionCode,
                                                Value = x.Value,
                                                ReasonIgnore = x.ReasonIgnore,
                                                RightStatus = (short)EnumJob.RightStatus.Correct
                                            }).ToList();

                                        ignoreItemInputParams.Add(new ItemInputParam
                                        {
                                            FilePartInstanceId = ignoreItemInputParam.FilePartInstanceId,
                                            DocTypeFieldId = ignoreItemInputParam.DocTypeFieldId,
                                            DocTypeFieldInstanceId = ignoreItemInputParam.DocTypeFieldInstanceId,
                                            DocTypeFieldCode = ignoreItemInputParam.DocTypeFieldCode,
                                            DocTypeFieldName = ignoreItemInputParam.DocTypeFieldName,
                                            DocTypeFieldSortOrder = ignoreItemInputParam.DocTypeFieldSortOrder,
                                            PrivateCategoryInstanceId = ignoreItemInputParam.PrivateCategoryInstanceId,
                                            InputType = ignoreItemInputParam.InputType,
                                            MinLength = ignoreItemInputParam.MinLength,
                                            MaxLength = ignoreItemInputParam.MaxLength,
                                            MaxValue = ignoreItemInputParam.MaxValue,
                                            MinValue = ignoreItemInputParam.MinValue,
                                            IsMultipleSelection = ignoreItemInputParam.IsMultipleSelection,
                                            CoordinateArea = ignoreItemInputParam.CoordinateArea,
                                            DocFieldValueId = ignoreItemInputParam.DocFieldValueId,
                                            DocFieldValueInstanceId = ignoreItemInputParam.DocFieldValueInstanceId,
                                            Value = inputParam.ActionCode == ActionCodeConstants.DataEntryBool ? true.ToString() : ignoreItemInputParam.Value, // Nếu là bước DataEntryBool thì coi như là Đúng luôn
                                            ConditionalValue = true.ToString(), // Meta ignore thì coi như là Đúng luôn
                                            OldValue = ignoreItemInputParam.Value,
                                            Price = ignoreItemInputParam.Price,
                                            IsDivergenceStep = ignoreIsDivergenceStep,
                                            ParallelJobInstanceId = ignoreParallelJobInstanceId,
                                            IsConvergenceNextStep = ignoreIsConvergenceNextStep,
                                            PrevJobInfos = prevIgnoreJobInfos
                                        });
                                    }
                                }

                                // TODO: Cập nhật thống kê, report

                                // Cập nhật giá trị DocFieldValue & Doc cho ignoreJobs
                                var ignoreItemDocFieldValueUpdateValues =
                                    ignoreJobs.Select(x => new ItemDocFieldValueUpdateValue
                                    {
                                        InstanceId = x.DocFieldValueInstanceId.GetValueOrDefault(),
                                        //Value = x.Value,
                                        Value = x.ActionCode == ActionCodeConstants.DataEntryBool
                                            ? x.OldValue
                                            : x.Value, // Trường hợp DataEntryBool thì lưu giá trị OldValue
                                        CoordinateArea = x.CoordinateArea,
                                        ActionCode = x.ActionCode
                                    }).ToList();
                                if (ignoreItemDocFieldValueUpdateValues.Any())
                                {
                                    var docFieldValueUpdateMultiValueEvt = new DocFieldValueUpdateMultiValueEvent
                                    {
                                        ItemDocFieldValueUpdateValues = ignoreItemDocFieldValueUpdateValues
                                    };
                                    // Outbox
                                    var outboxEntity = new OutboxIntegrationEvent
                                    {
                                        ExchangeName = nameof(DocFieldValueUpdateMultiValueEvent).ToLower(),
                                        ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                                        Data = JsonConvert.SerializeObject(docFieldValueUpdateMultiValueEvt),
                                        LastModificationDate = DateTime.UtcNow,
                                        Status = (short)EnumEventBus.PublishMessageStatus.Nack
                                    };
                                    try //try to publish event
                                    {
                                        _eventBus.Publish(docFieldValueUpdateMultiValueEvt, nameof(DocFieldValueUpdateMultiValueEvent).ToLower());
                                    }
                                    catch (Exception exPublishEvent)
                                    {
                                        Log.Error(exPublishEvent, "Error publish for event DocFieldValueUpdateMultiValueEvent ");

                                        try // try to save event to DB for retry later
                                        {
                                            await _outboxIntegrationEventRepository.AddAsync(outboxEntity);

                                        }
                                        catch (Exception exSaveDB)
                                        {
                                            Log.Error(exSaveDB, "Error save DB for event DocFieldValueUpdateMultiValueEvent ");
                                            throw;
                                        }
                                    }
                                }

                                // 1.2. Trigger next step ignore
                                if (nextWfsInfoes != null && nextWfsInfoes.All(x => x.ActionCode != ActionCodeConstants.End))
                                {
                                    foreach (var nextWfsInfo in nextWfsInfoes)
                                    {
                                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                                            ConfigStepPropertyConstants.IsPaidStep);
                                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                                        bool isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);

                                        decimal price = 0;
                                        string value = null;
                                        if (nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                        {
                                            foreach (var itemInput in ignoreItemInputParams)
                                            {
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
                                                                    inputParam.DigitizedTemplateInstanceId,
                                                                    itemInput.DocTypeFieldInstanceId)
                                                                : 0
                                                        }).ToList();
                                                }
                                                else
                                                {
                                                    itemInput.Price = isPaid
                                                        ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                                            inputParam.DigitizedTemplateInstanceId,
                                                            itemInput.DocTypeFieldInstanceId)
                                                        : 0;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            price = isPaid
                                                ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                                    inputParam.DigitizedTemplateInstanceId)
                                                : 0;

                                            var crrWfsJobsComplete = await _jobRepository.GetJobByWfs(
                                                inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.ActionCode,
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
                                                    var lstDocItemFull = new List<DocItem>();
                                                    var lstDocItemFullRs = await _docClientService.GetDocItemByDocInstanceId(inputParam.DocInstanceId.GetValueOrDefault(), accessToken);
                                                    if (lstDocItemFullRs.Success && lstDocItemFullRs.Data != null)
                                                    {
                                                        lstDocItemFull = lstDocItemFullRs.Data;
                                                    }
                                                    if (lstDocItemFull != null && lstDocItemFull.Any())
                                                    {
                                                        var existDocTypeFieldInstanceId = crrWfsJobsComplete.Select(x => x.DocTypeFieldInstanceId).ToList();
                                                        var missDocItem = lstDocItemFull.Where(x => !existDocTypeFieldInstanceId.Contains(x.DocTypeFieldInstanceId)).ToList();
                                                        if (missDocItem != null && missDocItem.Count > 0)
                                                        {
                                                            docItems.AddRange(missDocItem);
                                                        }
                                                    }
                                                }

                                                value = JsonConvert.SerializeObject(docItems);
                                            }
                                        }

                                        var ignoreInputParam = new InputParam
                                        {
                                            FileInstanceId = inputParam.FileInstanceId,
                                            ActionCode = nextWfsInfo.ActionCode,
                                            //ActionCodes = null,
                                            DocInstanceId = inputParam.DocInstanceId,
                                            DocName = inputParam.DocName,
                                            DocCreatedDate = inputParam.DocCreatedDate,
                                            DocPath = inputParam.DocPath,
                                            TaskId = inputParam.TaskId,
                                            TaskInstanceId = inputParam.TaskInstanceId,
                                            ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                            ProjectInstanceId = inputParam.ProjectInstanceId,
                                            SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                                            DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                                            DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                                            WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                            WorkflowStepInstanceId = nextWfsInfo.InstanceId,
                                            //WorkflowStepInstanceIds = null,
                                            //WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes),        // Không truyền thông tin này để giảm dung lượng msg
                                            //WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes), // Không truyền thông tin này để giảm dung lượng msg
                                            Value = value,
                                            Price = price,
                                            //WorkflowStepPrices = null,
                                            ClientTollRatio = inputParam.ClientTollRatio,
                                            WorkerTollRatio = inputParam.WorkerTollRatio,
                                            //IsDivergenceStep = ,
                                            //ParallelJobInstanceId = ,
                                            //IsConvergenceNextStep = ,
                                            TenantId = inputParam.TenantId,
                                            ItemInputParams = ignoreItemInputParams
                                        };

                                        var ignoreEvt = new TaskEvent
                                        {
                                            Input = JsonConvert.SerializeObject(ignoreInputParam),     // output của bước trước là input của bước sau
                                            AccessToken = accessToken
                                        };

                                        bool isTriggerNextStep = false;
                                        if (isNextStepRequiredAllBeforeStepComplete || _isParallelStep)
                                        {
                                            if (_isParallelStep)
                                            {
                                                var allIgnoreItemInputParams =
                                                    ignoreInputParam.ItemInputParams.Select(x => x).ToList();
                                                foreach (var itemInput in allIgnoreItemInputParams)
                                                {
                                                    bool hasJobWaitingOrProcessingByDocFieldValueAndParallelJob =
                                                        await _jobRepository
                                                            .CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(
                                                                inputParam.DocInstanceId.GetValueOrDefault(),
                                                                itemInput.DocFieldValueInstanceId,
                                                                itemInput.ParallelJobInstanceId);
                                                    if (!hasJobWaitingOrProcessingByDocFieldValueAndParallelJob)
                                                    {
                                                        var countOfExpectParallelJobs =
                                                            WorkflowHelper.CountOfExpectParallelJobs(wfsInfoes,
                                                                wfSchemaInfoes,
                                                                inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                                itemInput.DocTypeFieldInstanceId);
                                                        var parallelJobs =
                                                            await _jobRepository
                                                                .GetJobCompleteByDocFieldValueAndParallelJob(
                                                                    inputParam.DocInstanceId.GetValueOrDefault(),
                                                                    itemInput.DocFieldValueInstanceId,
                                                                    itemInput.ParallelJobInstanceId);
                                                        if (parallelJobs != null && parallelJobs.Count == countOfExpectParallelJobs)    // Số lượng parallelJobs = countOfExpectParallelJobs thì mới next step
                                                        {
                                                            // Xét trường hợp tất cả parallelJobs cùng done tại 1 thời điểm
                                                            bool triggerNextStepHappend = await TriggerNextStepHappened(
                                                                inputParam.DocInstanceId.GetValueOrDefault(),
                                                                inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                                itemInput.DocTypeFieldInstanceId,
                                                                itemInput.DocFieldValueInstanceId);
                                                            if (!triggerNextStepHappend)
                                                            {
                                                                // Điều chỉnh lại value của ItemInputParams cho evt
                                                                var oldValues = parallelJobs.Select(x => x.Value).ToList();
                                                                // Do parallelJobs ko tính các job có status = (short)EnumJob.Status.Ignore nên cần phải include vào oldValues
                                                                oldValues.Add(itemInput.Value);
                                                                var prevJobInfoes = parallelJobs.Select(x => new PrevJobInfo
                                                                {
                                                                    Id = x.Id.ToString(),
                                                                    UserInstanceId = x.UserInstanceId,
                                                                    WorkflowStepInstanceId = x.WorkflowStepInstanceId.GetValueOrDefault(),
                                                                    ActionCode = x.ActionCode,
                                                                    Value = x.Value,
                                                                    ReasonIgnore = x.ReasonIgnore,
                                                                    RightStatus = x.RightStatus
                                                                }).ToList();

                                                                // Trường hợp bước SAU là DataConfirmAuto & không phải luồng song song có Ocr & luồng có SyntheticOcr
                                                                if (nextWfsInfo.ActionCode == ActionCodeConstants.DataConfirmAuto &&
                                                                    parallelJobs.All(x =>
                                                                        x.ActionCode != ActionCodeConstants.Ocr) &&
                                                                    wfsInfoes.FirstOrDefault(x =>
                                                                        x.ActionCode == ActionCodeConstants.SyntheticOcr) != null)
                                                                {
                                                                    oldValues.Add(itemInput.OldValue);
                                                                    prevJobInfoes.Add(new PrevJobInfo
                                                                    {
                                                                        Value = itemInput.OldValue
                                                                    });
                                                                    itemInput.OldValue = itemInput.OldValue;
                                                                }

                                                                itemInput.Value = JsonConvert.SerializeObject(oldValues);
                                                                itemInput.PrevJobInfos = prevJobInfoes;
                                                                itemInput.IsConvergenceNextStep = true;
                                                                ignoreInputParam.ItemInputParams = new List<ItemInputParam> { itemInput };
                                                                ignoreEvt.Input = JsonConvert.SerializeObject(ignoreInputParam);

                                                                await TriggerNextStep(ignoreEvt, nextWfsInfo.ActionCode);
                                                                isTriggerNextStep = true;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else if (isNextStepRequiredAllBeforeStepComplete)
                                            {
                                                // Nếu bước TIẾP THEO yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
                                                var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, ignoreInputParam.WorkflowStepInstanceId.GetValueOrDefault(), true);
                                                // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                                                var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                                                if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                                                {
                                                    Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                                                    return new Tuple<bool, string, string>(false,
                                                        "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                                        null);
                                                }

                                                var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                                                var hasJobWaitingOrProcessing = await _jobRepository.CheckHasJobWaitingOrProcessingByMultiWfs(inputParam.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                                                if (!hasJobWaitingOrProcessing)
                                                {

                                                    // Xét trường hợp tất cả prevJobs cùng done tại 1 thời điểm
                                                    bool triggerNextStepHappend = await TriggerNextStepHappened(
                                                        inputParam.DocInstanceId.GetValueOrDefault(),
                                                        inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                                                    if (!triggerNextStepHappend)
                                                    {
                                                        await TriggerNextStep(ignoreEvt, nextWfsInfo.ActionCode);
                                                        isTriggerNextStep = true;
                                                    }

                                                }
                                            }
                                        }
                                        else
                                        {
                                            await TriggerNextStep(ignoreEvt, nextWfsInfo.ActionCode);
                                            isTriggerNextStep = true;
                                        }

                                        if (isTriggerNextStep)
                                        {
                                            Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                                        }

                                    }
                                }

                                jobs = allJobs.Where(x => x.Status == (short)EnumJob.Status.Waiting).ToList();
                                foreach (var job in jobs)
                                {
                                    var normalItemInputParam = inputParam.ItemInputParams.FirstOrDefault(x =>
                                        x.DocTypeFieldInstanceId == job.DocTypeFieldInstanceId &&
                                        x.DocFieldValueInstanceId == job.DocFieldValueInstanceId);
                                    if (normalItemInputParam != null)
                                    {
                                        normalItemInputParams.Add(normalItemInputParam);
                                    }
                                }

                                // Gán lại ItemInputParams
                                inputParam.ItemInputParams = normalItemInputParams;
                                @event.Input = JsonConvert.SerializeObject(inputParam);
                            }
                        }

                        // 1.2. TaskStepProgress: Update value
                        var updatedWaitingTaskStepProgress = new TaskStepProgress
                        {
                            Id = crrWfsInfo.Id,
                            InstanceId = crrWfsInfo.InstanceId,
                            Name = crrWfsInfo.Name,
                            ActionCode = crrWfsInfo.ActionCode,
                            WaitingJob = (short)jobs.Count,
                            ProcessingJob = 0,
                            CompleteJob = 0,
                            TotalJob = (short)jobs.Count,
                            Status = (short)EnumJob.Status.Waiting
                        };

                        var updateWaitingTaskResult = await _taskRepository.UpdateProgressValue(inputParam.TaskId, updatedWaitingTaskStepProgress);

                        string msgWatingJobs = jobs.Count == 1 ? "WaitingJob" : "WaitingJobs";
                        if (updateWaitingTaskResult != null)
                        {
                            Log.Logger.Information($"TaskStepProgress: +{jobs.Count} {msgWatingJobs} {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} success!");
                        }
                        else
                        {
                            Log.Logger.Error($"TaskStepProgress: +{jobs.Count} {msgWatingJobs} {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} failure!");
                        }

                        if (crrWfsInfo.IsAuto)
                        {
                            // 2. Process
                            // 2.1. Mark doc, task processing & update progress statistic
                            if (WorkflowHelper.IsMarkDocProcessing(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId))
                            {
                                var resultDocChangeProcessingStatus = await _docClientService.ChangeStatus(inputParam.DocInstanceId.GetValueOrDefault(), accessToken: accessToken);
                                if (!resultDocChangeProcessingStatus.Success)
                                {
                                    Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: Error change doc status!");
                                }

                                var resultTaskChangeProcessingStatus = await _taskRepository.ChangeStatus(inputParam.TaskId);
                                if (!resultTaskChangeProcessingStatus)
                                {
                                    Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: Error change task status!");
                                }

                                var changeProjectFileProgress = new ProjectFileProgress
                                {
                                    UnprocessedFile = -1,
                                    ProcessingFile = 1,
                                    CompleteFile = 0,
                                    TotalFile = 0,
                                    UnprocessedDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() },
                                    ProcessingDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() }
                                };
                                var changeProjectStepProgress = wfsInfoes
                                    .Where(x => x.InstanceId == inputParam.WorkflowStepInstanceId)
                                    .Select(x => new ProjectStepProgress
                                    {
                                        InstanceId = x.InstanceId,
                                        Name = x.Name,
                                        ActionCode = x.ActionCode,
                                        ProcessingFile = 1,
                                        CompleteFile = 0,
                                        TotalFile = 0,
                                        ProcessingDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() }
                                    }).ToList();
                                var changeProjectStatistic = new ProjectStatisticUpdateProgressDto
                                {
                                    ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                    ProjectInstanceId = inputParam.ProjectInstanceId.GetValueOrDefault(),
                                    WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                    WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                                    ActionCode = inputParam.ActionCode,
                                    DocInstanceId = inputParam.DocInstanceId.GetValueOrDefault(),
                                    StatisticDate = Int32.Parse(inputParam.DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                                    ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                                    ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                                    ChangeUserStatistic = JsonConvert.SerializeObject(new ProjectUser()),
                                    TenantId = inputParam.TenantId
                                };
                                await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);

                                Log.Logger.Information($"Published {nameof(ProjectStatisticUpdateProgressEvent)}: ProjectStatistic: +1 ProcessingFile for and StepProgressStatistic with DocInstanceId: {inputParam.DocInstanceId}");
                            }

                            // 2.2. Mark jobs processing
                            if (jobs.Any())
                            {
                                // Update current wfs status is processing
                                var resultDocChangeCurrentWfsInfoAuto = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(jobs.Select(x => x.DocInstanceId)), crrWfsInfo.Id, (short)EnumJob.Status.Processing, accessToken: accessToken);
                                if (!resultDocChangeCurrentWfsInfoAuto.Success)
                                {
                                    Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId {inputParam.DocInstanceId.GetValueOrDefault()} !");
                                }
                                foreach (var job in jobs)
                                {
                                    job.Status = (short)EnumJob.Status.Processing;
                                    job.ReceivedDate = DateTime.UtcNow;
                                    job.LastModificationDate = DateTime.UtcNow;
                                }

                                var resultUpdateJob = await _jobRepository.UpdateMultiAsync(jobs);

                                // TaskStepProgress: Update value
                                if (resultUpdateJob > 0)
                                {
                                    foreach (var job in jobs)
                                    {
                                        var updatedProcessingTaskStepProgress = new TaskStepProgress
                                        {
                                            Id = crrWfsInfo.Id,
                                            InstanceId = crrWfsInfo.InstanceId,
                                            Name = crrWfsInfo.Name,
                                            ActionCode = crrWfsInfo.ActionCode,
                                            WaitingJob = -1,
                                            ProcessingJob = 1,
                                            CompleteJob = 0,
                                            TotalJob = 0,
                                            Status = (short)EnumTaskStepProgress.Status.Processing
                                        };
                                        var updateProcessingTaskResult = await _taskRepository.UpdateProgressValue(job.TaskId.ToString(),
                                            updatedProcessingTaskStepProgress);

                                        if (updateProcessingTaskResult != null)
                                        {
                                            Log.Logger.Information($"TaskStepProgress: +{jobs.Count} ProcessingJob {inputParam.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} success!");
                                        }
                                        else
                                        {
                                            Log.Logger.Error($"TaskStepProgress: +{jobs.Count} ProcessingJob {inputParam.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} failure!");
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(crrWfsInfo.ServiceCode) && !string.IsNullOrEmpty(crrWfsInfo.ApiEndpoint) && !string.IsNullOrEmpty(@event.Input))
                            {
                                // TODO: Check lại hết case _isCreateMultiJobFile = true
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
                                int numOfProcessJob = _isCreateMultiJobFile ? inputParam.InputParams.Count : 1;

                                for (int i = 0; i < numOfProcessJob; i++)
                                {
                                    string input = _isCreateMultiJobFile ? JsonConvert.SerializeObject(inputParam.InputParams[i]) : @event.Input;
                                    var processJobResponse = await ProcessJob(input, serviceUri, crrWfsInfo.ApiEndpoint, crrWfsInfo.HttpMethodType, accessToken, ct);

                                    if (processJobResponse.Success && !string.IsNullOrEmpty(processJobResponse.Data))
                                    {
                                        // Cập nhật thanh toán tiền cho worker &  hệ thống bước HIỆN TẠI (SegmentLabeling)
                                        var clientInstanceId = await GetClientInstanceIdByProject(inputParam.ProjectInstanceId.GetValueOrDefault(), accessToken);
                                        if (clientInstanceId != Guid.Empty)
                                        {
                                            var itemTransactionAdds = new List<ItemTransactionAddDto>();
                                            var itemTransactionToSysWalletAdds = new List<ItemTransactionToSysWalletAddDto>();
                                            foreach (var job in jobs)
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
                                        short processingJob = _isCreateSingleJob ? (short)-1 : (short)-inputParam.ItemInputParams.Count;
                                        short completeJob = _isCreateSingleJob ? (short)1 : (short)inputParam.ItemInputParams.Count;
                                        short status = (short)EnumTaskStepProgress.Status.Complete;
                                        var attributeTypeChange = WorkflowHelper.GetAttributeTypeChange(wfsInfoes,
                                            wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                                        if (attributeTypeChange == (short)EnumWorkflowStep.AttributeTypeChange.MetaToMeta)
                                        {
                                            // Trường hợp Many jobs to many jobs, tính toán expect jobs
                                            var prevWfsInfo = prevWfsInfoes.FirstOrDefault();
                                            if (prevWfsInfo != null)
                                            {
                                                int expectAutoJobs;
                                                var prevWfsInstanceIds = new List<Guid> { prevWfsInfo.InstanceId };
                                                var prevJobs = await _jobRepository.GetJobByWfsInstanceIds(inputParam.DocInstanceId.GetValueOrDefault(), prevWfsInstanceIds);
                                                var crrCompleteJobs = await _jobRepository.GetJobByWfs(inputParam.DocInstanceId.GetValueOrDefault(), inputParam.ActionCode, inputParam.WorkflowStepInstanceId, (short)EnumJob.Status.Complete);
                                                var numOfResourceInJobPrevWfs = WorkflowHelper.GetNumOfResourceInJob(prevWfsInfo.ConfigStep);
                                                if (numOfResourceInJobPrevWfs > 1)
                                                {
                                                    expectAutoJobs = prevJobs.Count / numOfResourceInJobPrevWfs;
                                                }
                                                else
                                                {
                                                    expectAutoJobs = prevJobs.Count;
                                                }

                                                if (crrCompleteJobs.Count < expectAutoJobs)
                                                {
                                                    status = (short)EnumTaskStepProgress.Status.Processing;
                                                }
                                            }
                                        }
                                        var updateTaskStepProgress = new TaskStepProgress
                                        {
                                            Id = crrWfsInfo.Id,
                                            InstanceId = crrWfsInfo.InstanceId,
                                            Name = crrWfsInfo.Name,
                                            ActionCode = crrWfsInfo.ActionCode,
                                            WaitingJob = 0,
                                            ProcessingJob = processingJob,
                                            CompleteJob = completeJob,
                                            TotalJob = 0,
                                            Status = status
                                        };
                                        var updateCompleteTaskResult = await _taskRepository.UpdateProgressValue(inputParam.TaskId, updateTaskStepProgress);

                                        if (updateCompleteTaskResult != null)
                                        {
                                            Log.Logger.Information($"TaskStepProgress: +{jobs.Count} CompleteJob {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} success!");
                                        }
                                        else
                                        {
                                            Log.Logger.Error($"TaskStepProgress: +{jobs.Count} CompleteJob {inputParam.ActionCode} in TaskInstanceId: {inputParam.TaskInstanceId} with DocInstanceId: {inputParam.DocInstanceId} failure!");
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
                                                {inputParam.DocInstanceId.GetValueOrDefault()},
                                            CompleteDocInstanceIds = new List<Guid>
                                                {inputParam.DocInstanceId.GetValueOrDefault()}
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

                                        // 3.1. Cập nhật giá trị DocFieldValue & Doc
                                        var itemDocFieldValueUpdateValues = new List<ItemDocFieldValueUpdateValue>();
                                        if (_isCreateSingleJob)
                                        {
                                            var crrJob = jobs.FirstOrDefault();
                                            if (crrJob != null && !string.IsNullOrEmpty(crrJob.Value))
                                            {
                                                var crrDocItems = JsonConvert.DeserializeObject<List<DocItem>>(crrJob.Value);
                                                foreach (var docItem in crrDocItems)
                                                {
                                                    itemDocFieldValueUpdateValues.Add(new ItemDocFieldValueUpdateValue
                                                    {
                                                        InstanceId = docItem.DocFieldValueInstanceId.GetValueOrDefault(),
                                                        Value = docItem.Value,
                                                        CoordinateArea = docItem.CoordinateArea,
                                                        ActionCode = crrJob.ActionCode
                                                    });
                                                }
                                            }
                                        }
                                        else
                                        {
                                            foreach (var job in jobs)
                                            {
                                                itemDocFieldValueUpdateValues.Add(new ItemDocFieldValueUpdateValue
                                                {
                                                    InstanceId = job.DocFieldValueInstanceId.GetValueOrDefault(),
                                                    Value = job.Value,
                                                    CoordinateArea = job.CoordinateArea,
                                                    ActionCode = job.ActionCode
                                                });
                                            }
                                        }
                                        // 3.2. Cập nhật giá trị DocFieldValue & Doc
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
                                                Status = (short)EnumEventBus.PublishMessageStatus.Nack
                                            };

                                            try // try to publish event
                                            {
                                                _eventBus.Publish(docFieldValueUpdateMultiValueEvt, nameof(DocFieldValueUpdateMultiValueEvent).ToLower());
                                            }
                                            catch (Exception exPublishEvent)
                                            {
                                                Log.Error(exPublishEvent, $"Error publish for event DocFieldValueUpdateMultiValueEvent ");

                                                try // try to save event to DB for retry later
                                                {
                                                    await _outboxIntegrationEventRepository.AddAsync(outboxEntity);
                                                }
                                                catch (Exception exSaveDB)
                                                {
                                                    Log.Error(exSaveDB, $"Error save DB for event DocFieldValueUpdateMultiValueEvent ");
                                                    throw;
                                                }
                                            }


                                        }


                                        // 4. Trigger bước tiếp theo

                                        if (nextWfsInfoes != null && nextWfsInfoes.All(x => x.ActionCode != ActionCodeConstants.End))
                                        {
                                            // Xóa bỏ những dữ liệu không cần thiết
                                            processJobResponse.Data = RemoveUnwantedData(processJobResponse.Data);

                                            var currentJobResult = JsonConvert.DeserializeObject<InputParam>(processJobResponse.Data); //@event.output ở đây chính là kết quả của ProcessJob thực hiện ở trên
                                            var newListInputParam = new List<InputParam>();

                                            //Kiểm tra nếu bước hiện tại trả lại nhiều kết quả hay không 
                                            // ví dụ: OCR_A3 -> trả lại nhiều Doc-Copy cho 1 file-input -> thì cần tạo nhiều job
                                            if (currentJobResult.InputParams != null && currentJobResult.InputParams.Any())
                                            {
                                                newListInputParam.AddRange(currentJobResult.InputParams);
                                            }
                                            else
                                            {
                                                newListInputParam.Add(currentJobResult);
                                            }
                                            foreach (var nextWfsInfo in nextWfsInfoes)
                                            {
                                                foreach (var newInputParam in newListInputParam)
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
                                                            Input = processJobResponse.Data, // output của bước trước là input của bước sau
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
                                                                var oldItemInput = inputParam.ItemInputParams.FirstOrDefault(x =>
                                                                    x.DocTypeFieldInstanceId == itemInput.DocTypeFieldInstanceId &&
                                                                    x.DocFieldValueInstanceId == itemInput.DocFieldValueInstanceId);

                                                                bool hasJobWaitingOrProcessingByDocFieldValueAndParallelJob =
                                                                    await _jobRepository
                                                                        .CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(
                                                                            newInputParam.DocInstanceId.GetValueOrDefault(),
                                                                            itemInput.DocFieldValueInstanceId,
                                                                            oldItemInput?.ParallelJobInstanceId);
                                                                if (!hasJobWaitingOrProcessingByDocFieldValueAndParallelJob)
                                                                {
                                                                    var countOfExpectParallelJobs =
                                                                        WorkflowHelper.CountOfExpectParallelJobs(wfsInfoes,
                                                                            wfSchemaInfoes,
                                                                            inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                                            itemInput.DocTypeFieldInstanceId);
                                                                    var parallelJobs =
                                                                        await _jobRepository
                                                                            .GetJobCompleteByDocFieldValueAndParallelJob(
                                                                                inputParam.DocInstanceId.GetValueOrDefault(),
                                                                                itemInput.DocFieldValueInstanceId, oldItemInput?.ParallelJobInstanceId);
                                                                    if (parallelJobs.Count == countOfExpectParallelJobs)    // Số lượng parallelJobs = countOfExpectParallelJobs thì mới next step
                                                                    {
                                                                        // Xét trường hợp tất cả parallelJobs cùng done tại 1 thời điểm
                                                                        bool triggerNextStepHappend = await TriggerNextStepHappened(
                                                                            inputParam.DocInstanceId.GetValueOrDefault(),
                                                                            inputParam.WorkflowStepInstanceId.GetValueOrDefault(),
                                                                            itemInput.DocTypeFieldInstanceId,
                                                                            itemInput.DocFieldValueInstanceId);
                                                                        if (!triggerNextStepHappend)
                                                                        {
                                                                            // Điều chỉnh lại value của ItemInputParams cho evt
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
                                                        }
                                                        else if (isNextStepRequiredAllBeforeStepComplete)
                                                        {
                                                            // Nếu bước TIẾP THEO yêu cầu phải đợi tất cả các job ở bước TRƯỚC Complete thì mới trigger bước tiếp theo
                                                            var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), true);
                                                            // kiểm tra đã hoàn thành hết các meta chưa? không bao gồm các meta được đánh dấu bỏ qua
                                                            var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                                                            if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                                                            {
                                                                Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                                                                return new Tuple<bool, string, string>(false,
                                                                    "Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId",
                                                                    null);
                                                            }

                                                            var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                                                            var hasJobWaitingOrProcessing = await _jobRepository.CheckHasJobWaitingOrProcessingByMultiWfs(inputParam.DocInstanceId.GetValueOrDefault(), beforeWfsInfoIncludeCurrentStep, ignoreListDocTypeField);

                                                            if (!hasJobWaitingOrProcessing)
                                                            {

                                                                // Xét trường hợp tất cả prevJobs cùng done tại 1 thời điểm
                                                                bool triggerNextStepHappend = await TriggerNextStepHappened(
                                                                    inputParam.DocInstanceId.GetValueOrDefault(),
                                                                    inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                                                                if (!triggerNextStepHappend)
                                                                {
                                                                    await TriggerNextStep(evt, nextWfsInfo.ActionCode);
                                                                    isTriggerNextStep = true;
                                                                }

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
                                                        var resultDocChangeCurrentWfsInfoAuto = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                                        if (!resultDocChangeCurrentWfsInfoAuto.Success)
                                                        {
                                                            Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId {inputParam.DocInstanceId.GetValueOrDefault()} !");
                                                        }
                                                        Log.Logger.Information($"Published {nameof(TaskEvent)}: TriggerNextStep {nextWfsInfo.ActionCode}, WorkflowStepInstanceId: {nextWfsInfo.InstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                                                    }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Update current wfs status is complete
                                            var resultDocChangeCurrentWfsInfoAuto = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), crrWfsInfo.Id, (short)EnumJob.Status.Complete, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.Empty, null, accessToken: accessToken);
                                            if (!resultDocChangeCurrentWfsInfoAuto.Success)
                                            {
                                                Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId {inputParam.DocInstanceId.GetValueOrDefault()} !");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // crrWfsInfo is Manual
                            Log.Logger.Information($"Acked {nameof(TaskEvent)} step {inputParam.ActionCode}, WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} with DocInstanceId: {inputParam.DocInstanceId}");
                        }
                    }

                    sw.Stop();
                    Log.Information($"End handle TaskEvent Id {@event.EventBusIntergrationEventId.ToString()}- Elapsed time: {sw.ElapsedMilliseconds} ms ");
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

        /// <summary>
        /// Lấy danh sách các job trong DB để check exist trước khi tạo
        ///    check job exist by Project/Doc/Step/NumOfRound
        /// </summary>
        /// <param name="inputParam"></param>
        /// <param name="configStep"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private async Task<List<Job>> GetExisteJob(InputParam inputParam)
        {
            var existedJob = new List<Job>();

            if (_isCreateMultiJobFile)
            {
                foreach (var subInputParams in inputParam.InputParams)
                {
                    var tempListJob = await _jobRepository.GetAllJobByWfs(projectInstanceId: subInputParams.ProjectInstanceId.GetValueOrDefault(),
                                                            actionCode: inputParam.ActionCode,
                                                            workflowStepInstanceId: subInputParams.WorkflowStepInstanceId, status: null, docPath: string.Empty, batchJobInstanceId: null,
                                                            numOfRound: subInputParams.NumOfRound, docInstanceId: subInputParams.DocInstanceId);
                    if (tempListJob != null && tempListJob.Any())
                    {
                        existedJob.AddRange(tempListJob);
                    }
                }
            }
            else
            {
                existedJob = await _jobRepository.GetAllJobByWfs(projectInstanceId: inputParam.ProjectInstanceId.GetValueOrDefault(),
                                    actionCode: inputParam.ActionCode,
                                    workflowStepInstanceId: inputParam.WorkflowStepInstanceId, status: null, docPath: string.Empty, batchJobInstanceId: null,
                                    numOfRound: inputParam.NumOfRound, docInstanceId: inputParam.DocInstanceId);
            }
            return existedJob;
        }

        /// <summary>
        /// Tạo các job theo tham số 
        /// Nếu Job đã tồn tại thì không tạo mới
        /// Kết quả trả lại theo kiểu Tuple <isExist:boolen, List<job>
        /// </summary>
        /// <param name="inputParam"></param>
        /// <param name="configStep"></param>
        /// <param name="accessToken"></param>
        /// <returns>
        ///     Tuple <isExist:boolen, List<job>>
        /// </returns>
        private async Task<Tuple<bool, List<Job>>> CreateJobs(InputParam inputParam, string configStep, string accessToken)
        {
            var jobs = new List<Job>();
            try
            {
                //check job exist
                var randomDelayTime = new Random().Next(10, 100); // 10-100 ms
                await Task.Delay(randomDelayTime);
                var existedJob = await GetExisteJob(inputParam);

                bool isExistedJob;
                if (existedJob.Any()) // job existed ; do not create duplicate
                {
                    isExistedJob = true;
                    return new Tuple<bool, List<Job>>(isExistedJob, existedJob);
                }

                if (_isCreateSingleJob)
                {
                    string value = null;
                    if (inputParam.IsConvergenceNextStep && !string.IsNullOrEmpty(inputParam.Value))
                    {
                        var values = JsonConvert.DeserializeObject<List<string>>(inputParam.Value);
                        if (values != null && values.Any())
                        {
                            values = values.Distinct().ToList();
                            // Nếu bước HIỆN TẠI là hội tụ và tất cả các giá trị trong bước trước giống nhau thì lấy luôn giá trị đó làm khởi tạo
                            if (values.Count == 1)
                            {
                                value = values.First();
                            }
                        }
                    }

                    if (_isCreateMultiJobFile)
                    {
                        // Create multi jobs (File)
                        foreach (var subInputParams in inputParam.InputParams)
                        {
                            jobs.Add(new Job
                            {
                                InstanceId = Guid.NewGuid(),
                                Code = $"J{await _sequenceJobRepository.GetSequenceValue(SequenceJobNameConstants.SequenceJobName)}",
                                TurnInstanceId = Guid.NewGuid(),
                                DocInstanceId = subInputParams.DocInstanceId,
                                DocName = subInputParams.DocName,
                                DocCreatedDate = subInputParams.DocCreatedDate,
                                DocPath = subInputParams.DocPath,
                                SyncMetaId = SyncPathHelper.GetSyncMetaId(subInputParams.DocPath),
                                TaskId = !string.IsNullOrEmpty(subInputParams.TaskId) ? new ObjectId(subInputParams.TaskId) : ObjectId.Empty,
                                TaskInstanceId = subInputParams.TaskInstanceId,
                                DocTypeFieldInstanceId = null,
                                DocTypeFieldName = null,
                                DocTypeFieldSortOrder = 0,
                                InputType = 0,
                                PrivateCategoryInstanceId = null,
                                IsMultipleSelection = null,
                                DocFieldValueInstanceId = null,
                                ProjectTypeInstanceId = subInputParams.ProjectTypeInstanceId,
                                ProjectInstanceId = subInputParams.ProjectInstanceId,
                                SyncTypeInstanceId = subInputParams.SyncTypeInstanceId,
                                DigitizedTemplateInstanceId = subInputParams.DigitizedTemplateInstanceId,
                                DigitizedTemplateCode = subInputParams.DigitizedTemplateCode,
                                WorkflowInstanceId = subInputParams.WorkflowInstanceId,
                                WorkflowStepInstanceId = subInputParams.WorkflowStepInstanceId,
                                Value = subInputParams.IsConvergenceNextStep ? RemoveUnwantedInputParamValue(value) : RemoveUnwantedInputParamValue(subInputParams.Value),
                                OldValue = RemoveUnwantedInputParamValue(subInputParams.Value),
                                //Input = null,
                                ActionCode = subInputParams.ActionCode,
                                //UserInstanceId = null,
                                FileInstanceId = subInputParams.FileInstanceId,
                                //FilePartInstanceId = null,
                                //CoordinateArea = null,
                                RandomSortOrder = new Random().Next(1, int.MaxValue),
                                Price = subInputParams.Price,
                                ClientTollRatio = subInputParams.ClientTollRatio,
                                WorkerTollRatio = subInputParams.WorkerTollRatio,
                                CreatedDate = DateTime.UtcNow,
                                //ShareJobSortOrder = 0,
                                IsParallelJob = _isParallelStep,
                                ParallelJobInstanceId = _isParallelStep ? subInputParams.ParallelJobInstanceId : null,
                                Note = subInputParams.Note,
                                NumOfRound = subInputParams.NumOfRound,
                                BatchName = subInputParams.BatchName,
                                BatchJobInstanceId = subInputParams.BatchJobInstanceId,
                                QaStatus = subInputParams.QaStatus,
                                TenantId = subInputParams.TenantId,
                                Status = (short)EnumJob.Status.Waiting,
                                LastModifiedBy = subInputParams.LastModifiedBy,
                            });
                        }
                    }
                    else
                    {
                        // Create a single job
                        jobs.Add(new Job
                        {
                            InstanceId = Guid.NewGuid(),
                            Code = $"J{await _sequenceJobRepository.GetSequenceValue(SequenceJobNameConstants.SequenceJobName)}",
                            TurnInstanceId = Guid.NewGuid(),
                            DocInstanceId = inputParam.DocInstanceId,
                            DocName = inputParam.DocName,
                            DocCreatedDate = inputParam.DocCreatedDate,
                            DocPath = inputParam.DocPath,
                            SyncMetaId = SyncPathHelper.GetSyncMetaId(inputParam.DocPath),
                            TaskId = !string.IsNullOrEmpty(inputParam.TaskId) ? new ObjectId(inputParam.TaskId) : ObjectId.Empty,
                            TaskInstanceId = inputParam.TaskInstanceId,
                            DocTypeFieldInstanceId = null,
                            DocTypeFieldName = null,
                            DocTypeFieldSortOrder = 0,
                            InputType = 0,
                            PrivateCategoryInstanceId = null,
                            IsMultipleSelection = null,
                            DocFieldValueInstanceId = null,
                            ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                            ProjectInstanceId = inputParam.ProjectInstanceId,
                            SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                            DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                            DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                            WorkflowInstanceId = inputParam.WorkflowInstanceId,
                            WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                            Value = inputParam.IsConvergenceNextStep ? RemoveUnwantedInputParamValue(value) : RemoveUnwantedInputParamValue(inputParam.Value),
                            OldValue = RemoveUnwantedInputParamValue(inputParam.Value),
                            //Input = null,
                            ActionCode = inputParam.ActionCode,
                            //UserInstanceId = null,
                            FileInstanceId = inputParam.FileInstanceId,
                            //FilePartInstanceId = null,
                            //CoordinateArea = null,
                            RandomSortOrder = new Random().Next(1, int.MaxValue),
                            Price = inputParam.Price,
                            ClientTollRatio = inputParam.ClientTollRatio,
                            WorkerTollRatio = inputParam.WorkerTollRatio,
                            CreatedDate = DateTime.UtcNow,
                            //ShareJobSortOrder = 0,
                            IsParallelJob = _isParallelStep,
                            ParallelJobInstanceId = _isParallelStep ? inputParam.ParallelJobInstanceId : null,
                            Note = inputParam.Note,
                            NumOfRound = inputParam.NumOfRound,
                            BatchName = inputParam.BatchName,
                            BatchJobInstanceId = inputParam.BatchJobInstanceId,
                            QaStatus = inputParam.QaStatus,
                            TenantId = inputParam.TenantId,
                            Status = (short)EnumJob.Status.Waiting,
                            LastModifiedBy = inputParam.LastModifiedBy,
                        });
                    }
                }
                else if (inputParam.ItemInputParams != null && inputParam.ItemInputParams.Any())
                {
                    //Không tạo các job phân mảnh đối với các Meta được đánh dấu không hiện thị để nhập
                    var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                    if (listDocTypeFieldResponse == null || !listDocTypeFieldResponse.Success)
                    {
                        Log.Error("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                    }

                    var listEnableInputDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == true).ToList();

                    // Create multi jobs
                    foreach (var itemInput in inputParam.ItemInputParams.Where(x => listEnableInputDocTypeField.Any(e => e.Id == x.DocTypeFieldId)))
                    {
                        var isIgnoreStep = WorkflowHelper.IsIgnoreStep(configStep,
                            itemInput.DocTypeFieldInstanceId.GetValueOrDefault());

                        string value = itemInput.Value;
                        string oldValue = itemInput.Value;

                        // Set Value & OldValue cho 1 số case đặc biệt
                        if (inputParam.ActionCode == ActionCodeConstants.DataConfirmBoolAuto)
                        {
                            value = itemInput.OldValue;
                            itemInput.ConditionalValue = itemInput.Value;
                        }

                        // Trường hợp bước HIỆN TẠI là hội tụ
                        if (itemInput.IsConvergenceNextStep && !string.IsNullOrEmpty(itemInput.Value))
                        {

                            if (inputParam.ActionCode != ActionCodeConstants.DataConfirmBoolAuto)
                            {
                                var values = JsonConvert.DeserializeObject<List<string>>(itemInput.Value);
                                if (values != null && values.Any())
                                {
                                    values = values.Distinct().ToList();
                                    // Nếu tất cả các giá trị trong bước trước giống nhau thì lấy luôn giá trị đó làm khởi tạo
                                    if (values.Count == 1)
                                    {
                                        value = values.First();
                                    }
                                    else
                                    {
                                        value = null;
                                    }
                                }
                            }
                        }

                        // Trường hợp bước HIỆN TẠI là DataEntryBool và là bước bỏ qua thì gán luôn giá trị bằng True
                        if (isIgnoreStep && inputParam.ActionCode == ActionCodeConstants.DataEntryBool)
                        {
                            value = true.ToString();
                        }

                        // Trường hợp bước HIỆN TẠI là DataConfirm or DataConfirmAuto or DataConfirmBoolAuto và ko phải là bước hội tụ
                        if ((inputParam.ActionCode == ActionCodeConstants.DataConfirm ||
                             inputParam.ActionCode == ActionCodeConstants.DataConfirmAuto ||
                             inputParam.ActionCode == ActionCodeConstants.DataConfirmBoolAuto) &&
                            !itemInput.IsConvergenceNextStep && !string.IsNullOrEmpty(itemInput.Value))
                        {
                            oldValue = JsonConvert.SerializeObject(new List<string> { itemInput.Value });
                        }

                        int numOfCloneJob = _numOfResourceInJob > 0 ? _numOfResourceInJob : 1;
                        for (int i = 0; i < numOfCloneJob; i++)
                        {
                            jobs.Add(new Job
                            {
                                InstanceId = Guid.NewGuid(),
                                Code = $"J{await _sequenceJobRepository.GetSequenceValue(SequenceJobNameConstants.SequenceJobName)}",
                                //TurnInstanceId = null,
                                DocInstanceId = inputParam.DocInstanceId,
                                DocName = inputParam.DocName,
                                DocCreatedDate = inputParam.DocCreatedDate,
                                DocPath = inputParam.DocPath,
                                SyncMetaId = SyncPathHelper.GetSyncMetaId(inputParam.DocPath),
                                TaskId = !string.IsNullOrEmpty(inputParam.TaskId) ? new ObjectId(inputParam.TaskId) : ObjectId.Empty,
                                TaskInstanceId = inputParam.TaskInstanceId,
                                DocTypeFieldInstanceId = itemInput.DocTypeFieldInstanceId,
                                DocTypeFieldCode = itemInput.DocTypeFieldCode,
                                DocTypeFieldName = itemInput.DocTypeFieldName,
                                DocTypeFieldSortOrder = itemInput.DocTypeFieldSortOrder,
                                InputType = itemInput.InputType,
                                MaxLength = itemInput.MaxLength,
                                MinLength = itemInput.MinLength,
                                MaxValue = itemInput.MaxValue,
                                MinValue = itemInput.MinValue,
                                PrivateCategoryInstanceId = itemInput.PrivateCategoryInstanceId,
                                IsMultipleSelection = itemInput.IsMultipleSelection,
                                DocFieldValueInstanceId = itemInput.DocFieldValueInstanceId,
                                ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                                ProjectInstanceId = inputParam.ProjectInstanceId,
                                SyncTypeInstanceId = inputParam.SyncTypeInstanceId,
                                DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                                DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                                WorkflowInstanceId = inputParam.WorkflowInstanceId,
                                WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                                Value = value,
                                OldValue = oldValue,
                                //Input = null,
                                ActionCode = inputParam.ActionCode,
                                //UserInstanceId = null,
                                FileInstanceId = inputParam.FileInstanceId,
                                FilePartInstanceId = itemInput.FilePartInstanceId,
                                CoordinateArea = itemInput.CoordinateArea,
                                RandomSortOrder = new Random().Next(1, int.MaxValue),
                                Price = itemInput.Price,
                                ClientTollRatio = inputParam.ClientTollRatio,
                                WorkerTollRatio = inputParam.WorkerTollRatio,
                                CreatedDate = DateTime.UtcNow,
                                ShareJobSortOrder = _numOfResourceInJob > 1 ? (short)(i + 1) : (short)0,
                                IsParallelJob = _isParallelStep,
                                ParallelJobInstanceId = _isParallelStep ? itemInput.ParallelJobInstanceId : null,
                                RightStatus = isIgnoreStep ? (short)EnumJob.RightStatus.Correct : (short)EnumJob.RightStatus.WaitingConfirm,
                                Note = inputParam.Note,
                                NumOfRound = inputParam.NumOfRound,
                                BatchName = inputParam.BatchName,
                                BatchJobInstanceId = inputParam.BatchJobInstanceId,
                                TenantId = inputParam.TenantId,
                                Status = isIgnoreStep ? (short)EnumJob.Status.Ignore : (short)EnumJob.Status.Waiting
                            });
                        }
                    }
                }

                if (jobs.Any())
                {

                    //check existed job sencond time => to minimize duplicate job 
                    await Task.Delay(randomDelayTime);
                    existedJob = await GetExisteJob(inputParam);
                    if (existedJob.Any())
                    {
                        isExistedJob = true;
                        return new Tuple<bool, List<Job>>(isExistedJob, existedJob);
                    }

                    // Create job
                    jobs = await _jobRepository.AddMultiAsyncV2(jobs);
                    Log.Logger.Information($"Created {jobs.Count} jobs in step {inputParam.ActionCode} with DocInstanceId: {inputParam.DocInstanceId}");

                    isExistedJob = false;
                    return new Tuple<bool, List<Job>>(isExistedJob, jobs);
                }
                else
                {
                    Log.Logger.Information($"There isn't not any jobs with DocInstanceId: {inputParam.DocInstanceId}");
                    isExistedJob = false;
                    return new Tuple<bool, List<Job>>(isExistedJob, jobs);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.Message);
                throw;
            }
        }

        private async Task<GenericResponse<string>> ProcessJob(string input, string serviceUri, string apiEndpoint, short httpMethodType = (short)HttpClientMethodType.POST, string accessToken = null, CancellationToken ct = default)
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

                // Update current wfs status is error
                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), null, string.IsNullOrEmpty(inputParam.Note) ? string.Empty : inputParam.Note, null, accessToken: accessToken);
                if (!resultDocChangeCurrentWfsInfo.Success)
                {
                    Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change current work flow step info for DocInstanceId: {inputParam.DocInstanceId.GetValueOrDefault()} !");
                }
                // Mark doc, task error, job error
                var resultDocChangeErrorStatus = await _docClientService.ChangeStatus(inputParam.DocInstanceId.GetValueOrDefault(), (short)EnumDoc.Status.Error, accessToken);
                if (!resultDocChangeErrorStatus.Success)
                {
                    Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: Error change doc status!");
                }

                var resultTaskChangeErrorStatus = await _taskRepository.ChangeStatus(inputParam.TaskId, (short)EnumTask.Status.Error);
                if (!resultTaskChangeErrorStatus)
                {
                    Log.Logger.Error($"{nameof(TaskIntegrationEventHandler)}: Error change task status!");
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
                        ProcessingFile = -1,
                        CompleteFile = 0,
                        ErrorFile = 1,
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
                        //ErrorFile = 1,
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

                throw;
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
                Status = (short)EnumEventBus.PublishMessageStatus.Nack,
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
                var triggerNextStepHappened = await _cachingHelper.TryGetFromCacheAsync<string>(cacheKey);  // Lưu countOfExpectJobs
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

        private async Task<List<QueueLock>> CreateQueueLockes(InputParam inputParam)
        {
            if (inputParam != null)
            {
                inputParam.IsFromQueueLock = true;
                var queueLockes = new List<QueueLock>();
                if (_isCreateSingleJob)
                {
                    // Create a single queueLock
                    queueLockes.Add(new QueueLock
                    {
                        FileInstanceId = inputParam.FileInstanceId,
                        DocInstanceId = inputParam.DocInstanceId,
                        DocName = inputParam.DocName,
                        DocCreatedDate = inputParam.DocCreatedDate,
                        DocPath = inputParam.DocPath,
                        ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                        ProjectInstanceId = inputParam.ProjectInstanceId,
                        DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                        DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                        WorkflowInstanceId = inputParam.WorkflowInstanceId,
                        DocTypeFieldInstanceId = null,
                        DocTypeFieldSortOrder = 0,
                        DocFieldValueInstanceId = null,
                        WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                        ActionCode = inputParam.ActionCode,
                        InputParam = JsonConvert.SerializeObject(inputParam)
                    });
                }
                else if (inputParam.ItemInputParams != null && inputParam.ItemInputParams.Any())
                {
                    // Create multi queueLockes
                    foreach (var itemInput in inputParam.ItemInputParams)
                    {
                        queueLockes.Add(new QueueLock
                        {
                            FileInstanceId = inputParam.FileInstanceId,
                            DocInstanceId = inputParam.DocInstanceId,
                            DocName = inputParam.DocName,
                            DocCreatedDate = inputParam.DocCreatedDate,
                            DocPath = inputParam.DocPath,
                            ProjectTypeInstanceId = inputParam.ProjectTypeInstanceId,
                            ProjectInstanceId = inputParam.ProjectInstanceId,
                            DigitizedTemplateInstanceId = inputParam.DigitizedTemplateInstanceId,
                            DigitizedTemplateCode = inputParam.DigitizedTemplateCode,
                            WorkflowInstanceId = inputParam.WorkflowInstanceId,
                            DocTypeFieldInstanceId = itemInput.DocTypeFieldInstanceId,
                            DocTypeFieldSortOrder = itemInput.DocTypeFieldSortOrder,
                            DocFieldValueInstanceId = itemInput.DocFieldValueInstanceId,
                            WorkflowStepInstanceId = inputParam.WorkflowStepInstanceId,
                            ActionCode = inputParam.ActionCode,
                            InputParam = JsonConvert.SerializeObject(inputParam)
                        });
                    }
                }

                if (_useCache)
                {
                    // Check các queueLockes đã lưu vào DB trước đó hay chưa, nếu chưa thì mới thêm mới
                    var existedQueueLockes = new List<QueueLock>();
                    foreach (var queueLock in queueLockes)
                    {
                        var digitizedTemplateInstanceId = queueLock.DigitizedTemplateInstanceId == null
                            ? "null"
                            : queueLock.DigitizedTemplateInstanceId.ToString();
                        var docTypeFieldInstanceId = queueLock.DocTypeFieldInstanceId == null
                            ? "null"
                            : queueLock.DocTypeFieldInstanceId.ToString();
                        var docFieldValueInstanceId = queueLock.DocFieldValueInstanceId == null
                            ? "null"
                            : queueLock.DocFieldValueInstanceId.ToString();

                        string cacheKey =
                            $"{queueLock.ProjectInstanceId}_{digitizedTemplateInstanceId}_{queueLock.WorkflowStepInstanceId}_{queueLock.DocInstanceId}_{docTypeFieldInstanceId}_{docFieldValueInstanceId}_{queueLock.Status}";
                        var existedQueue = await _cachingHelper.TryGetFromCacheAsync<string>(cacheKey);
                        if (!string.IsNullOrEmpty(existedQueue))
                        {
                            existedQueueLockes.Add(queueLock);
                        }
                    }

                    if (existedQueueLockes.Any())
                    {
                        queueLockes = queueLockes.Except(existedQueueLockes).ToList();
                    }

                    var result = await _queueLockRepository.AddMultiAsync(queueLockes);

                    if (result > 0)
                    {
                        // Lưu các queueLockes đã thêm vào DB trên cache
                        foreach (var queueLock in queueLockes)
                        {
                            var digitizedTemplateInstanceId = queueLock.DigitizedTemplateInstanceId == null
                                ? "null"
                                : queueLock.DigitizedTemplateInstanceId.ToString();
                            var docTypeFieldInstanceId = queueLock.DocTypeFieldInstanceId == null
                                ? "null"
                                : queueLock.DocTypeFieldInstanceId.ToString();
                            var docFieldValueInstanceId = queueLock.DocFieldValueInstanceId == null
                                ? "null"
                                : queueLock.DocFieldValueInstanceId.ToString();

                            string cacheKey =
                                $"{queueLock.ProjectInstanceId}_{digitizedTemplateInstanceId}_{queueLock.WorkflowStepInstanceId}_{queueLock.DocInstanceId}_{docTypeFieldInstanceId}_{docFieldValueInstanceId}_{queueLock.Status}";
                            await _cachingHelper.TrySetCacheAsync(cacheKey, queueLock.Id.ToString(), 1800);
                        }
                    }
                }
                else
                {
                    return await _queueLockRepository.UpSertMultiQueueLockAsync(queueLockes);
                }
            }

            return null;
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

            if (inputParam.InputParams != null && inputParam.InputParams.Any() && wfsInfoes != null && wfSchemaInfoes != null)
            {
                foreach (var item in inputParam.InputParams)
                {
                    if (string.IsNullOrEmpty(item.WorkflowStepInfoes) ||
                        string.IsNullOrEmpty(item.WorkflowSchemaInfoes))
                    {
                        item.WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes);
                        item.WorkflowSchemaInfoes = JsonConvert.SerializeObject(wfSchemaInfoes);
                        result = true;
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

        #region Remove unwanted data

        private string RemoveUnwantedData(string processJobResponseData)
        {
            if (!string.IsNullOrEmpty(processJobResponseData))
            {
                var inputParam = JsonConvert.DeserializeObject<InputParam>(processJobResponseData);
                if (inputParam != null)
                {
                    if (!string.IsNullOrEmpty(inputParam.WorkflowStepInfoes))
                    {
                        inputParam.WorkflowStepInfoes = null;
                    }

                    if (!string.IsNullOrEmpty(inputParam.WorkflowSchemaInfoes))
                    {
                        inputParam.WorkflowSchemaInfoes = null;
                    }

                    if (inputParam.InputParams != null && inputParam.InputParams.Any())
                    {
                        foreach (var item in inputParam.InputParams)
                        {
                            if (!string.IsNullOrEmpty(item.WorkflowStepInfoes))
                            {
                                item.WorkflowStepInfoes = null;
                            }

                            if (!string.IsNullOrEmpty(item.WorkflowSchemaInfoes))
                            {
                                item.WorkflowSchemaInfoes = null;
                            }
                        }
                    }

                    return JsonConvert.SerializeObject(inputParam);
                }

                return null;
            }

            return null;
        }

        private string RemoveUnwantedInputParamValue(string inputParamValue)
        {
            if (!string.IsNullOrEmpty(inputParamValue))
            {
                var docItems = JsonConvert.DeserializeObject<List<DocItem>>(inputParamValue);
                if (docItems != null && docItems.Any())
                {
                    var storedDocItems = _mapper.Map<List<DocItem>, List<StoredDocItem>>(docItems);
                    return JsonConvert.SerializeObject(storedDocItems);
                }

                return null;
            }

            return null;
        }
        #endregion
    }
}
