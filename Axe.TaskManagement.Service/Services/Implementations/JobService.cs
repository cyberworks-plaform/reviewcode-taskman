﻿using AutoMapper;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Ce.Auth.Client.Dtos;
using Ce.Auth.Client.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Common.Lib.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Ce.Workflow.Client.Dtos;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using MiniExcelLibs;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    /// <summary>
    /// Initialize
    /// </summary>
    public partial class JobService : MongoBaseService<Job, JobDto>, IJobService
    {
        private readonly IJobRepository _repository;
        private readonly ITaskRepository _taskRepository;
        private readonly IEventBus _eventBus;
        private readonly IComplainRepository _complainRepository;
        private readonly IDocClientService _docClientService;
        private readonly IUserConfigClientService _userConfigClientService;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IWorkflowStepClientService _workflowStepClientService;
        private readonly IWorkflowStepTypeClientService _workflowStepTypeClientService;
        private readonly IProjectClientService _projectClientService;
        private readonly IProjectTypeClientService _projectTypeClientService;
        private readonly IAppUserClientService _appUserClientService;
        private readonly IProjectStatisticClientService _projectStatisticClientService;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IMoneyService _moneyService;
        private readonly IDocTypeFieldClientService _docTypeFieldClientService;
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly IExternalProviderServiceConfigClientService _providerConfig;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

        private readonly ICachingHelper _cachingHelper;
        private readonly IRecallJobWorkerService _recallJobWorkerService;
        private readonly bool _useCache;
        private const string DataProcessing = "DataProcessing";
        private const int TimeOut = 600;   // Default HttpClient timeout is 100s
        public JobService(
            IJobRepository repos,
            IMapper mapper,
            IUserPrincipalService userPrincipalService,
            IComplainRepository complainRepository,
            IDocClientService docClientService,
            IUserConfigClientService userConfigClientService,
            IWorkflowClientService workflowClientService,
            ICachingHelper cachingHelper,
            IEventBus eventBus,
            ITaskRepository taskRepository,
            IProjectTypeClientService projectTypeClientService,
            IProjectClientService projectClientService,
            IWorkflowStepTypeClientService workflowStepTypeClientService,
            IWorkflowStepClientService workflowStepClientService,
            IAppUserClientService appUserClientService,
            IUserProjectClientService userProjectClientService,
            ITransactionClientService transactionClientService,
            IProjectStatisticClientService projectStatisticClientService,
            IMoneyService moneyService,
            IDocTypeFieldClientService docTypeFieldClientService,
            ISequenceJobRepository sequenceJobRepository,
            IBaseHttpClientFactory clientFatory,
            IExternalProviderServiceConfigClientService externalProviderServiceConfigClientService,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IRecallJobWorkerService recallJobWorkerService
            ) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
            _cachingHelper = cachingHelper;
            _eventBus = eventBus;
            _projectTypeClientService = projectTypeClientService;
            _projectClientService = projectClientService;
            _complainRepository = complainRepository;
            _docClientService = docClientService;
            _workflowStepTypeClientService = workflowStepTypeClientService;
            _workflowStepClientService = workflowStepClientService;
            _taskRepository = taskRepository;
            _workflowClientService = workflowClientService;
            _userConfigClientService = userConfigClientService;
            _appUserClientService = appUserClientService;
            _projectStatisticClientService = projectStatisticClientService;
            _transactionClientService = transactionClientService;
            _moneyService = moneyService;
            _docTypeFieldClientService = docTypeFieldClientService;
            _useCache = _cachingHelper != null;
            _clientFatory = clientFatory;
            _providerConfig = externalProviderServiceConfigClientService;
            _configuration = configuration;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _recallJobWorkerService = recallJobWorkerService;
        }

        /// <summary>
        /// Đồng bộ các Job.Status = waiting từ TaskMan -> Job Distribution
        /// Admin system có thể dùng hàm này huống Job Dis bị mất đồng bộ với TaskMan
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<GenericResponse> ResyncJobDistribution(Guid projectInstanceId, string actionCode)
        {
            var response = new GenericResponse();
            string msg = string.Empty;
            string cacheKey = $"ResyncJobDistribution_{projectInstanceId}_{actionCode}";
            try
            {
                //Check runing status: kiểm tra xem có đang chạy job này không
                var isRuning = _cachingHelper.TryGetFromCache<bool>(cacheKey);
                if (isRuning)
                {
                    response = GenericResponse.ResultWithData(null, "Yêu cầu này đã được gửi trước đây và hệ thống đang thực hiện...");
                    return response;
                }

                //Set runing statu: Nếu chưa chạy thì bắt đầu ghi nhận -> ghi vào cache -> tự expired trong thời gian 24h
                isRuning = true;
                _cachingHelper.TrySetCache(cacheKey, isRuning, 24 * 60 * 60);

                var totalJob = 0;
                //lấy các job của project đang ở trạng thái waiting
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                filter = filter & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);

                if (!string.IsNullOrEmpty(actionCode))
                {
                    filter = filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }

                var findOption = new FindOptions<Job>()
                {
                    BatchSize = 1000
                };

                using (var jobCursor = await _repository.GetCursorListJobAsync(filter, findOption))
                {
                    var jobs = new List<Job>();

                    var pageSize = 100;

                    while (jobCursor.MoveNext())
                    {
                        jobs.AddRange(jobCursor.Current);

                        //gửi theo từng bó 100 job sang distribution
                        var totalPage = jobs.Count / pageSize;
                        if (jobs.Count % pageSize != 0)
                        {
                            totalPage += 1;
                        }
                        for (var page = 0; page < totalPage; page++)
                        {
                            // Publish message sang DistributionJob
                            var batchjobs = jobs.Skip(page * pageSize).Take(pageSize).ToList();
                            var logJobEvt = new LogJobEvent
                            {
                                LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(batchjobs),
                            };
                            try
                            {
                                var isAcked = false;
                                try //try first time
                                {
                                    isAcked = _eventBus.Publish(logJobEvt, nameof(LogJobEvent).ToLower());
                                }
                                catch (Exception ex) // wait 1 second then try again
                                {
                                    Task.Delay(1000).Wait();
                                    isAcked = _eventBus.Publish(logJobEvt, nameof(LogJobEvent).ToLower());
                                }
                                finally
                                {
                                    if (isAcked)
                                    {
                                        totalJob += jobs.Count;
                                    }
                                }

                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Error:{ex.Message}");
                                Log.Information("List job send error");
                                foreach (var item in batchjobs)
                                {
                                    Log.Information($"JobID:{item.Id} - JobCode:{item.Code} - Project: {item.ProjectInstanceId} - Actioncode:{item.ActionCode}");
                                }
                            }

                            logJobEvt.LogJobs.Clear();
                            logJobEvt = null;
                            batchjobs.Clear();
                        }
                        jobs.Clear();
                        msg = string.Format("Đã thực hiện gửi {0} job sang Job Distribution. Đang tiếp tục...", totalJob);
                        Log.Information(msg);
                    }
                }

                if (totalJob <= 0)
                {
                    msg = "Không có job nào đang ở trạng thái chờ phân phối";
                }
                else
                {
                    msg = string.Format("Đã hoàn thành thực hiện gửi {0} job sang Job Distribution.", totalJob);
                }

                response.Success = true;
                response.Message = msg;
                Log.Logger.Information(msg);

            }
            catch (Exception ex)
            {
                msg = "Lỗi khi thực hiện ResyncJobDistribution:" + ex.Message;
                response.Success = false;
                response.Message = msg;
                response.Error = ex.StackTrace;
                Log.Logger.Error(ex, msg);
            }
            finally
            {
                //clear runing status
                _cachingHelper.RemoveCache(cacheKey);
            }

            return response;

        }

    }

    /// <summary>
    /// Process Job
    /// </summary>
    public partial class JobService
    {
        #region ProcessSegmentLabeling

        public async Task<GenericResponse<int>> ProcessSegmentLabeling(JobResult result, string accessToken = null)
        {
            // 2. Process
            GenericResponse<int> response;
            try
            {
                var parse = ObjectId.TryParse(result.JobId, out ObjectId id);
                if (!parse)
                {
                    return GenericResponse<int>.ResultWithData(-1, "Không parse được Id");
                }
                var docItems = JsonConvert.DeserializeObject<List<DocItem>>(result.Value);

                if (docItems == null || docItems.Count == 0 || docItems.All(x => string.IsNullOrEmpty(x.CoordinateArea)))
                {
                    return GenericResponse<int>.ResultWithData(-1, "Dữ liệu không chính xác");
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.Eq(s => s.Id, id); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.SegmentLabeling)); // ActionCode

                var job = await _repos.FindFirstAsync(filter & filter2 & filter3 & filter4);

                if (job == null)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                if (job.DueDate < DateTime.UtcNow)
                {
                    await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);

                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời hạn");
                    return response;
                }

                var now = DateTime.UtcNow;
                var updatedValue = JsonConvert.SerializeObject(docItems);
                job.HasChange = updatedValue != job.Value;
                job.Value = updatedValue;
                job.Status = (short)EnumJob.Status.Complete;
                job.LastModificationDate = now;
                job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                job.RightStatus = (short)EnumJob.RightStatus.Correct;

                var resultUpdateJob = await _repos.ReplaceOneAsync(filter2, job);
                if (resultUpdateJob != null)
                {
                    // Trigger after jobs submit
                    var evt = new AfterProcessSegmentLabelingEvent
                    {
                        //Job = _mapper.Map<Job, JobDto>(resultUpdateJob),
                        JobId = resultUpdateJob.Id.ToString(),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<AfterProcessSegmentLabelingEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJob = _mapper.Map<Job, LogJobDto>(job);
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = new List<LogJobDto> { logJob },
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    response = GenericResponse<int>.ResultWithData(1);
                }
                else
                {
                    Log.Error($"ProcessSegmentLabeling fail: job => {job.Code}");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessSegmentLabeling => param: {JsonConvert.SerializeObject(result)}; Message: {ex.Message}; StackTrace:{ex.StackTrace}");
            }

            return response;
        }

        #endregion

        #region ProcessDataEntry

        public async Task<GenericResponse<int>> ProcessDataEntry(List<JobResult> result, string accessToken = null)
        {
            // 2. Process
            Stopwatch sw = new Stopwatch();
            sw.Start();
            GenericResponse<int> response;
            try
            {
                var lstId = result.Select(x => ObjectId.Parse(x.JobId)).ToList();
                if (lstId.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.In(x => x.Id, lstId); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.DataEntry)); // ActionCode

                var jobs = await _repos.FindAsync(filter & filter2 & filter3 & filter4);

                if (jobs == null || jobs.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                if (jobs.Exists(x => x.DueDate < DateTime.UtcNow))
                {
                    var userId_turnIDHashSet = new HashSet<string>();
                    foreach (var job in jobs)
                    {
                        //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                        var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                        if (!userId_turnIDHashSet.Contains(hashkey))
                        {
                            userId_turnIDHashSet.Add(hashkey);
                            await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                        }
                    }
                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời hạn");
                    return response;
                }

                var workflowInstanceId = jobs.Select(x => x.WorkflowInstanceId.GetValueOrDefault()).FirstOrDefault();
                var wfInfoes = await GetWfInfoes(workflowInstanceId, accessToken);
                var wfsInfoes = wfInfoes.Item1;
                var wfSchemaInfoes = wfInfoes.Item2;

                foreach (var job in jobs)
                {
                    var now = DateTime.UtcNow;
                    var oldValue = job.Value;
                    var newValue = result.FirstOrDefault(x => x.JobId == job.Id.ToString())?.Value;
                    job.HasChange = newValue != job.Value;
                    job.Value = newValue;
                    job.RightStatus = (short)EnumJob.RightStatus.Confirmed;
                    job.Status = (short)EnumJob.Status.Complete;
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                    if (job.IsIgnore)
                    {
                        job.Value = null;
                        job.Price = 0;
                    }
                    else
                    {
                        // Tính toán Price
                        var itemWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == job.WorkflowStepInstanceId);
                        var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                            job.WorkflowStepInstanceId.GetValueOrDefault());
                        var prevWfsInfo = prevWfsInfoes.FirstOrDefault();

                        var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValue, job.Value);
                        job.Price = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                            job.DigitizedTemplateInstanceId, job.DocTypeFieldInstanceId,
                            isPriceEdit);
                    }
                }

                var resultUpdateJob = await _repos.UpdateMultiAsync(jobs);
                if (resultUpdateJob > 0)
                {
                    // Update current wfs status is processing
                    var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(jobs.Select(x => x.DocInstanceId.GetValueOrDefault())), -1, (short)EnumJob.Status.Processing, accessToken: accessToken);
                    if (!resultDocChangeCurrentWfsInfo.Success)
                    {
                        Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change multi current work flow step info for DocInstanceIds {JsonConvert.SerializeObject(jobs.Select(x => x.DocInstanceId.GetValueOrDefault()))} !");
                    }
                    // Trigger after jobs submit
                    var evt = new AfterProcessDataEntryEvent
                    {
                        //Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs),
                        JobIds = jobs.Select(x => x.Id.ToString()).ToList(),
                        AccessToken = accessToken
                    };

                    // Outbox AfterProcessDataEntryEvent
                    await PublishEvent<AfterProcessDataEntryEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
                        AccessToken = accessToken
                    };

                    // Outbox LogJob
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    response = GenericResponse<int>.ResultWithData(resultUpdateJob);
                }
                else
                {
                    var jobCodes = string.Join(',', jobs.Select(x => x.Code));
                    Log.Error($"ProcessDataEntry: {jobCodes} failure!");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessSendDataEntry => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }
            sw.Stop();
            Log.Debug($"ProcessDataEntry - Save job result in duration: {sw.ElapsedMilliseconds} ms");
            return response;
        }

        #endregion

        #region ProcessDataEntryBool

        public async Task<GenericResponse<int>> ProcessDataEntryBool(List<JobResult> result, string accessToken = null)
        {
            // 2. Process
            GenericResponse<int> response;
            try
            {
                var lstId = result.Select(x => ObjectId.Parse(x.JobId)).ToList();
                if (lstId.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.In(x => x.Id, lstId); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.DataEntryBool)); // ActionCode

                var jobs = await _repos.FindAsync(filter & filter2 & filter3 & filter4);

                if (jobs == null || jobs.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                if (jobs.Exists(x => x.DueDate < DateTime.UtcNow))
                {
                    var userId_turnIDHashSet = new HashSet<string>();
                    foreach (var job in jobs)
                    {
                        //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                        var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                        if (!userId_turnIDHashSet.Contains(hashkey))
                        {
                            userId_turnIDHashSet.Add(hashkey);
                            await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                        }
                    }
                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời hạn");
                    return response;
                }

                var workflowInstanceId = jobs.Select(x => x.WorkflowInstanceId.GetValueOrDefault()).FirstOrDefault();
                var wfInfoes = await GetWfInfoes(workflowInstanceId, accessToken);
                var wfsInfoes = wfInfoes.Item1;
                var wfSchemaInfoes = wfInfoes.Item2;

                foreach (var job in jobs)
                {
                    var now = DateTime.UtcNow;
                    var oldValue = job.Value;
                    var newValue = result.FirstOrDefault(x => x.JobId == job.Id.ToString())?.Value;
                    job.HasChange = newValue != job.Value;
                    job.Value = newValue;
                    job.Status = (short)EnumJob.Status.Complete;
                    job.RightStatus = (short)EnumJob.RightStatus.Confirmed;
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                    if (job.IsIgnore)
                    {
                        job.Value = null;
                        job.Price = 0;
                    }
                    else
                    {
                        // Tính toán Price
                        var itemWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == job.WorkflowStepInstanceId);
                        var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                            job.WorkflowStepInstanceId.GetValueOrDefault());
                        var prevWfsInfo = prevWfsInfoes.FirstOrDefault();

                        var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValue, job.Value);
                        job.Price = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                            job.DigitizedTemplateInstanceId, job.DocTypeFieldInstanceId,
                            isPriceEdit);
                    }
                }

                var resultUpdateJob = await _repos.UpdateMultiAsync(jobs);
                if (resultUpdateJob > 0)
                {
                    // Trigger after jobs submit
                    var evt = new AfterProcessDataEntryBoolEvent
                    {
                        //Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs),
                        JobIds = jobs.Select(x => x.Id.ToString()).ToList(),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<AfterProcessDataEntryBoolEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    response = GenericResponse<int>.ResultWithData(resultUpdateJob);
                }
                else
                {
                    var jobCodes = string.Join(',', jobs.Select(x => x.Code));
                    Log.Error($"ProcessSendDataEntryBool: {jobCodes} failure!");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessSendDataEntryBool => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }

            return response;
        }

        #endregion

        #region ProcessDataConfirmBoolAuto

        public async Task<GenericResponse<string>> ProcessDataConfirmBool(ModelInput model, string accessToken = null, CancellationToken ct = default)
        {
            if (model == null || string.IsNullOrEmpty(model.Input))
            {
                return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
            }

            // 2. Process
            GenericResponse<string> response;
            try
            {
                var inputParam = JsonConvert.DeserializeObject<InputParam>(model.Input);
                if (inputParam == null || inputParam.FileInstanceId == null || inputParam.DocInstanceId == null || inputParam.ItemInputParams == null || !inputParam.ItemInputParams.Any())
                {
                    return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
                }

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        ct.ThrowIfCancellationRequested();
                        return GenericResponse<string>.ResultWithError((int)HttpStatusCode.RequestTimeout, null, "Request has been cancelled");
                    }

                    var itemInputParams = inputParam.ItemInputParams;
                    var docItems = new List<DocItem>();
                    var filter1 = Builders<Job>.Filter.Eq(x => x.FileInstanceId, inputParam.FileInstanceId);
                    var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, inputParam.ActionCode);
                    var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);

                    var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                    var wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);
                    var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                    var prevWfsInfo = prevWfsInfoes.First();
                    int numOfResourceInJobPrevStep = WorkflowHelper.GetNumOfResourceInJob(prevWfsInfo.ConfigStep);
                    foreach (var itemInput in itemInputParams)
                    {
                        if (numOfResourceInJobPrevStep > 1)
                        {
                            var strValues = JsonConvert.DeserializeObject<List<string>>(itemInput.Value);
                            var values = strValues.Select(x => Boolean.Parse(x)).ToList();
                            if (values != null && values.Any() && values.All(x => x))
                            {
                                itemInput.ConditionalValue = true.ToString();
                            }
                            else
                            {
                                itemInput.ConditionalValue = false.ToString();
                            }
                        }
                        else
                        {
                            itemInput.ConditionalValue = itemInput.Value;
                        }

                        // Gán lại giá trị OldValue cho Value
                        itemInput.Value = itemInput.OldValue;

                        docItems.Add(new DocItem
                        {
                            FilePartInstanceId = itemInput.FilePartInstanceId,
                            DocTypeFieldId = itemInput.DocTypeFieldId,
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
                            DocFieldValueId = itemInput.DocFieldValueId,
                            DocFieldValueInstanceId = itemInput.DocFieldValueInstanceId,
                            CoordinateArea = itemInput.CoordinateArea,
                            Value = itemInput.Value
                        });

                        //Cập nhật giá trị value
                        var updateValue = Builders<Job>.Update
                            .Set(s => s.RightStatus, itemInput.ConditionalValue == true.ToString() ? (short)EnumJob.RightStatus.Correct : (short)EnumJob.RightStatus.Confirmed)
                            .Set(s => s.Status, (short)EnumJob.Status.Complete);

                        var resultUpdateJob = await _repos.UpdateOneAsync(filter1 & filter2 & filter3, updateValue);
                    }

                    var updatedValue = JsonConvert.SerializeObject(docItems);

                    if (wfsInfoes != null && wfsInfoes.Any() && wfSchemaInfoes != null && wfSchemaInfoes.Any())
                    {
                        var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                        var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                        var nextWfsInfo = nextWfsInfoes.First();
                        bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                        var isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                        bool isConvergenceNextStep = isParallelStep;
                        var parallelJobInstanceId = Guid.NewGuid();

                        int numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(nextWfsInfo.ConfigStep);
                        bool isDivergenceStep = isMultipleNextStep || numOfResourceInJob > 1;

                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                            ConfigStepPropertyConstants.IsPaidStep);
                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                        // Tổng hợp price cho các bước TIẾP THEO
                        decimal price = 0;
                        List<WorkflowStepPrice> wfsPrices = null;
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
                                                inputParam.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                            : 0
                                    }).ToList();
                                }
                                else
                                {
                                    itemInput.Price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                            inputParam.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                        : 0;
                                }
                            }
                        }
                        else
                        {
                            if (isMultipleNextStep)
                            {
                                wfsPrices = nextWfsInfoes.Select(x => new WorkflowStepPrice
                                {
                                    InstanceId = x.InstanceId,
                                    ActionCode = x.ActionCode,
                                    Price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPriceV2(x.ConfigPrice,
                                            inputParam.DigitizedTemplateInstanceId)
                                        : 0
                                }).ToList();
                            }
                            else
                            {
                                price = isPaid ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice, inputParam.DigitizedTemplateInstanceId) : 0;
                            }
                        }

                        // Tổng hợp value
                        string value;
                        var isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                        if (isNextStepRequiredAllBeforeStepComplete)
                        {
                            var listDocTypeFieldRs = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                            if (listDocTypeFieldRs.Success && listDocTypeFieldRs.Data.Any())
                            {
                                var listDocTypeFields = _docTypeFieldClientService.ConvertToDocItem(listDocTypeFieldRs.Data);
                                foreach (var field in listDocTypeFields)
                                {
                                    if (!docItems.Any(x => x.DocTypeFieldInstanceId == field.DocTypeFieldInstanceId))
                                    {
                                        docItems.Add(field); // add missing field
                                    }
                                }
                            }

                            value = JsonConvert.SerializeObject(docItems);
                        }
                        else
                        {
                            value = updatedValue;
                        }

                        var output = new InputParam
                        {
                            FileInstanceId = inputParam.FileInstanceId,
                            ActionCode = isMultipleNextStep ? null : nextWfsInfo.ActionCode,
                            ActionCodes = isMultipleNextStep ? nextWfsInfoes.Select(x => x.ActionCode).ToList() : null,
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
                            WorkflowStepInstanceId = isMultipleNextStep ? null : nextWfsInfo.InstanceId,
                            WorkflowStepInstanceIds = isMultipleNextStep
                                ? nextWfsInfoes.Select(x => (Guid?)x.InstanceId).ToList()
                                : null,
                            //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                            //WorkflowSchemaInfoes = inputParam.WorkflowSchemaInfoes, // Không truyền thông tin này để giảm dung lượng msg
                            Value = value,
                            Price = price,
                            WorkflowStepPrices = wfsPrices,
                            ClientTollRatio = inputParam.ClientTollRatio,
                            WorkerTollRatio = inputParam.WorkerTollRatio,
                            IsDivergenceStep = crrWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                               isDivergenceStep,
                            ParallelJobInstanceId =
                                nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                    ? parallelJobInstanceId
                                    : null,
                            IsConvergenceNextStep = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                    isConvergenceNextStep,
                            TenantId = inputParam.TenantId,
                            ItemInputParams = itemInputParams
                        };

                        if (ct.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                            return GenericResponse<string>.ResultWithError((int)HttpStatusCode.RequestTimeout, null, "Request has been cancelled");
                        }

                        return GenericResponse<string>.ResultWithData(JsonConvert.SerializeObject(output));
                    }
                    return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Can not get list WorkflowStepInfo!");
                }

            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.StackTrace);
                Log.Logger.Error(ex.Message);
                response = GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        #endregion

        #region ProcessDataCheck

        public async Task<GenericResponse<int>> ProcessDataCheck(List<JobResult> result, string accessToken = null)
        {
            var lstId = result.Select(x => ObjectId.Parse(x.JobId)).ToList();

            // 2. Process
            GenericResponse<int> response;
            try
            {
                if (lstId.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.In(x => x.Id, lstId); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.DataCheck)); // ActionCode

                var jobs = await _repos.FindAsync(filter & filter2 & filter3 & filter4);

                if (jobs == null || jobs.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }
                if (jobs.Exists(x => x.DueDate < DateTime.UtcNow))
                {
                    var userId_turnIDHashSet = new HashSet<string>();
                    foreach (var job in jobs)
                    {
                        //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                        var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                        if (!userId_turnIDHashSet.Contains(hashkey))
                        {
                            userId_turnIDHashSet.Add(hashkey);
                            await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                        }
                    }

                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời gian");
                    return response;
                }

                var workflowInstanceId = jobs.Select(x => x.WorkflowInstanceId.GetValueOrDefault()).FirstOrDefault();
                var wfInfoes = await GetWfInfoes(workflowInstanceId, accessToken);
                var wfsInfoes = wfInfoes.Item1;
                var wfSchemaInfoes = wfInfoes.Item2;

                foreach (var job in jobs)
                {
                    var now = DateTime.UtcNow;
                    var oldValue = job.Value;
                    var newValue = result.FirstOrDefault(x => x.JobId == job.Id.ToString())?.Value;
                    job.HasChange = newValue != job.Value;
                    job.Value = newValue;
                    job.Status = (short)EnumJob.Status.Complete;
                    job.RightStatus = (short)EnumJob.RightStatus.Confirmed;
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                    if (job.IsIgnore)
                    {
                        job.Value = null;
                        job.Price = 0;
                    }
                    else
                    {
                        // Tính toán Price
                        var itemWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == job.WorkflowStepInstanceId);
                        var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                            job.WorkflowStepInstanceId.GetValueOrDefault());
                        var prevWfsInfo = prevWfsInfoes.FirstOrDefault();

                        var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValue, job.Value);
                        job.Price = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                            job.DigitizedTemplateInstanceId, job.DocTypeFieldInstanceId,
                            isPriceEdit);
                    }
                }

                var resultUpdateJob = await _repos.UpdateMultiAsync(jobs);
                if (resultUpdateJob > 0)
                {
                    // Trigger after jobs submit
                    var evt = new AfterProcessDataCheckEvent
                    {
                        //Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs),
                        JobIds = jobs.Select(x => x.Id.ToString()).ToList(),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<AfterProcessDataCheckEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    response = GenericResponse<int>.ResultWithData(resultUpdateJob);
                }
                else
                {
                    var jobCodes = string.Join(',', jobs.Select(x => x.Code));
                    Log.Error($"ProcessDataCheck: {jobCodes} failure!");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {

                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessDataCheck => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }

            return response;
        }

        #endregion

        #region ProcessDataConfirm

        public async Task<GenericResponse<int>> ProcessDataConfirm(List<JobResult> result, string accessToken = null)
        {
            var lstId = result.Select(x => ObjectId.Parse(x.JobId)).ToList();

            // 2. Process
            GenericResponse<int> response;
            try
            {
                if (lstId.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.In(x => x.Id, lstId); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.DataConfirm)); // ActionCode

                var jobs = await _repos.FindAsync(filter & filter2 & filter3 & filter4);

                if (jobs == null || jobs.Count == 0)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }
                if (jobs.Exists(x => x.DueDate < DateTime.UtcNow))
                {
                    var userId_turnIDHashSet = new HashSet<string>();
                    foreach (var job in jobs)
                    {
                        //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                        var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                        if (!userId_turnIDHashSet.Contains(hashkey))
                        {
                            userId_turnIDHashSet.Add(hashkey);
                            await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                        }
                    }

                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời gian");
                    return response;
                }

                foreach (var job in jobs)
                {
                    var now = DateTime.UtcNow;
                    var oldValue = job.Value;
                    var newValue = result.FirstOrDefault(x => x.JobId == job.Id.ToString())?.Value;
                    job.HasChange = newValue != job.Value;
                    job.Value = newValue;
                    job.Status = (short)EnumJob.Status.Complete;
                    job.RightStatus = (short)EnumJob.RightStatus.Correct;
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                }

                var resultUpdateJob = await _repos.UpdateMultiAsync(jobs);
                if (resultUpdateJob > 0)
                {
                    // Trigger after jobs submit
                    var evt = new AfterProcessDataConfirmEvent
                    {
                        //Jobs = _mapper.Map<List<Job>, List<JobDto>>(jobs),
                        JobIds = jobs.Select(x => x.Id.ToString()).ToList(),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<AfterProcessDataConfirmEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    response = GenericResponse<int>.ResultWithData(resultUpdateJob);
                }
                else
                {
                    var jobCodes = string.Join(',', jobs.Select(x => x.Code));
                    Log.Error($"ProcessDataConfirm: {jobCodes} failure!");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {

                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessDataConfirm => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }

            return response;
        }

        #endregion

        #region ProcessDataConfirmAuto

        public async Task<GenericResponse<string>> ProcessDataConfirmAuto(ModelInput model, string accessToken = null, CancellationToken ct = default)
        {
            if (model == null || string.IsNullOrEmpty(model.Input))
            {
                return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
            }
            try
            {
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        ct.ThrowIfCancellationRequested();
                        return GenericResponse<string>.ResultWithError((int)HttpStatusCode.RequestTimeout, null, "Request has been cancelled");
                    }

                    var inputParam = JsonConvert.DeserializeObject<InputParam>(model.Input);
                    if (inputParam == null || inputParam.FileInstanceId == null || inputParam.DocInstanceId == null || inputParam.ItemInputParams == null || !inputParam.ItemInputParams.Any())
                    {
                        return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
                    }

                    //ConfirmAuto
                    const double exactRatio = 0.9;  // Fix lấy giá trị độ chính xác là 90%
                    var itemInputParams = inputParam.ItemInputParams;
                    if (itemInputParams == null || itemInputParams.Count == 0)
                    {
                        return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
                    }

                    var docItems = new List<DocItem>();
                    var filter1 = Builders<Job>.Filter.Eq(x => x.FileInstanceId, inputParam.FileInstanceId);
                    var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, inputParam.ActionCode);
                    var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
                    var updatePrevJobInfos = new List<PrevJobInfo>();
                    foreach (var itemInput in itemInputParams)
                    {
                        if (itemInput.PrevJobInfos != null && itemInput.PrevJobInfos.Any())
                        {
                            var confirmAutoInputDto = new ConfirmAutoInputDto
                            {
                                data = new List<ItemConfirmAutoInputDto>()
                            };

                            int i = 0;
                            foreach (var prevJobInfo in itemInput.PrevJobInfos)
                            {
                                confirmAutoInputDto.data.Add(new ItemConfirmAutoInputDto
                                {
                                    labeler_id = i,
                                    label_value = prevJobInfo.Value
                                });
                                i++;
                            }

                            // Call CyberLab ConfirmAuto
                            var confirmAutoOutput = await GetConfirmAutoResult(confirmAutoInputDto, accessToken);
                            if (confirmAutoOutput != null && !string.IsNullOrEmpty(confirmAutoOutput.true_label))
                            {
                                var confirmValue = confirmAutoOutput.true_label;
                                itemInput.Value = confirmValue;
                                if (confirmAutoOutput.confidence >= exactRatio)
                                {
                                    itemInput.ConditionalValue = true.ToString();
                                }
                                else
                                {
                                    itemInput.ConditionalValue = false.ToString();
                                }

                                docItems.Add(new DocItem
                                {
                                    FilePartInstanceId = itemInput.FilePartInstanceId,
                                    DocTypeFieldId = itemInput.DocTypeFieldId,
                                    DocTypeFieldInstanceId = itemInput.DocTypeFieldInstanceId,
                                    DocTypeFieldName = itemInput.DocTypeFieldName,
                                    DocTypeFieldSortOrder = itemInput.DocTypeFieldSortOrder,
                                    InputType = itemInput.InputType,
                                    MaxLength = itemInput.MaxLength,
                                    MinLength = itemInput.MinLength,
                                    MaxValue = itemInput.MaxValue,
                                    MinValue = itemInput.MinValue,
                                    PrivateCategoryInstanceId = itemInput.PrivateCategoryInstanceId,
                                    IsMultipleSelection = itemInput.IsMultipleSelection,
                                    DocFieldValueId = itemInput.DocFieldValueId,
                                    DocFieldValueInstanceId = itemInput.DocFieldValueInstanceId,
                                    CoordinateArea = itemInput.CoordinateArea,
                                    Value = confirmValue
                                });

                                //Cập nhật giá trị value
                                var updateValue = Builders<Job>.Update
                                    .Set(s => s.RightStatus, itemInput.ConditionalValue == true.ToString() ? (short)EnumJob.RightStatus.Correct : (short)EnumJob.RightStatus.Wrong)
                                    .Set(s => s.Value, confirmValue)
                                    .Set(s => s.Status, (short)EnumJob.Status.Complete);

                                var resultUpdateJob = await _repos.UpdateOneAsync(filter1 & filter2 & filter3, updateValue);

                                // Cập nhật RightStatus cho prevJobInfos
                                foreach (var prevJobInfo in itemInput.PrevJobInfos)
                                {
                                    prevJobInfo.RightStatus = prevJobInfo.Value == confirmAutoOutput.true_label
                                        ? (short)EnumJob.RightStatus.Correct
                                        : (short)EnumJob.RightStatus.Wrong;
                                    if (!string.IsNullOrEmpty(prevJobInfo.Id))
                                    {
                                        updatePrevJobInfos.Add(prevJobInfo);
                                    }
                                }
                            }
                            else
                            {
                                //Cập nhật giá trị value
                                var updateValue = Builders<Job>.Update
                                    .Set(s => s.RightStatus, (short)EnumJob.RightStatus.Wrong)
                                    .Set(s => s.Value, null)
                                    .Set(s => s.Status, (short)EnumJob.Status.Error);

                                var resultUpdateJob = await _repos.UpdateOneAsync(filter1 & filter2 & filter3, updateValue);

                                // Nếu giá trị confirmAuto trả về là null thì bung Exception
                                throw new Exception();
                            }
                        }
                    }

                    // Cập nhật RightStatus cho prevJobs
                    if (updatePrevJobInfos.Any())
                    {
                        var lstId = updatePrevJobInfos.Select(x => ObjectId.Parse(x.Id)).ToList();
                        var filterId = Builders<Job>.Filter.In(x => x.Id, lstId); // lấy theo id
                        var prevJobs = await _repos.FindAsync(filterId);
                        foreach (var prevJob in prevJobs)
                        {
                            var updatePrevJobInfo = updatePrevJobInfos.FirstOrDefault(x => x.Id == prevJob.Id.ToString());
                            prevJob.RightStatus = updatePrevJobInfo?.RightStatus ?? (short)EnumJob.RightStatus.Confirmed;
                        }
                        await _repos.UpdateMultiAsync(prevJobs);
                    }

                    var updatedValue = JsonConvert.SerializeObject(docItems);

                    var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                    var wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);
                    if (wfsInfoes != null && wfsInfoes.Any() && wfSchemaInfoes != null && wfSchemaInfoes.Any())
                    {
                        var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == inputParam.WorkflowStepInstanceId);
                        var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                        var nextWfsInfo = nextWfsInfoes.First();
                        bool isMultipleNextStep = nextWfsInfoes.Count > 1;
                        var isParallelStep = WorkflowHelper.IsParallelStep(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                        bool isConvergenceNextStep = isParallelStep;
                        var parallelJobInstanceId = Guid.NewGuid();

                        int numOfResourceInJob = WorkflowHelper.GetNumOfResourceInJob(nextWfsInfo.ConfigStep);
                        bool isDivergenceStep = isMultipleNextStep || numOfResourceInJob > 1;

                        var strIsPaidStep = WorkflowHelper.GetConfigStepPropertyValue(nextWfsInfo.ConfigStep,
                            ConfigStepPropertyConstants.IsPaidStep);
                        var isPaidStepRs = Boolean.TryParse(strIsPaidStep, out bool isPaidStep);
                        bool isPaid = !nextWfsInfo.IsAuto || (nextWfsInfo.IsAuto && isPaidStepRs && isPaidStep);

                        // Tổng hợp price cho các bước TIẾP THEO
                        decimal price = 0;
                        List<WorkflowStepPrice> wfsPrices = null;
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
                                                inputParam.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                            : 0
                                    }).ToList();
                                }
                                else
                                {
                                    itemInput.Price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice,
                                            inputParam.DigitizedTemplateInstanceId, itemInput.DocTypeFieldInstanceId)
                                        : 0;
                                }
                            }
                        }
                        else
                        {
                            if (isMultipleNextStep)
                            {
                                wfsPrices = nextWfsInfoes.Select(x => new WorkflowStepPrice
                                {
                                    InstanceId = x.InstanceId,
                                    ActionCode = x.ActionCode,
                                    Price = isPaid
                                        ? MoneyHelper.GetPriceByConfigPriceV2(x.ConfigPrice,
                                            inputParam.DigitizedTemplateInstanceId)
                                        : 0
                                }).ToList();
                            }
                            else
                            {
                                price = isPaid ? MoneyHelper.GetPriceByConfigPriceV2(nextWfsInfo.ConfigPrice, inputParam.DigitizedTemplateInstanceId) : 0;
                            }
                        }

                        // Tổng hợp value
                        string value;
                        var isNextStepRequiredAllBeforeStepComplete = WorkflowHelper.IsRequiredAllBeforeStepComplete(wfsInfoes, wfSchemaInfoes, nextWfsInfo.InstanceId);
                        if (isNextStepRequiredAllBeforeStepComplete)
                        {
                            var listDocTypeFieldRs = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                            if (listDocTypeFieldRs.Success && listDocTypeFieldRs.Data.Any())
                            {
                                var listDocTypeFields = _docTypeFieldClientService.ConvertToDocItem(listDocTypeFieldRs.Data);
                                foreach (var field in listDocTypeFields)
                                {
                                    if (!docItems.Any(x => x.DocTypeFieldInstanceId == field.DocTypeFieldInstanceId))
                                    {
                                        docItems.Add(field); // add missing field
                                    }
                                }
                            }
                            value = JsonConvert.SerializeObject(docItems);
                        }
                        else
                        {
                            value = updatedValue;
                        }

                        var output = new InputParam
                        {
                            FileInstanceId = inputParam.FileInstanceId,
                            ActionCode = isMultipleNextStep ? null : nextWfsInfo.ActionCode,
                            ActionCodes = isMultipleNextStep ? nextWfsInfoes.Select(x => x.ActionCode).ToList() : null,
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
                            WorkflowStepInstanceId = isMultipleNextStep ? null : nextWfsInfo.InstanceId,
                            WorkflowStepInstanceIds = isMultipleNextStep
                                ? nextWfsInfoes.Select(x => (Guid?)x.InstanceId).ToList()
                                : null,
                            //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                            //WorkflowSchemaInfoes = inputParam.WorkflowSchemaInfoes, // Không truyền thông tin này để giảm dung lượng msg
                            Value = value,
                            Price = price,
                            WorkflowStepPrices = wfsPrices,
                            ClientTollRatio = inputParam.ClientTollRatio,
                            WorkerTollRatio = inputParam.WorkerTollRatio,
                            IsDivergenceStep = crrWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                               isDivergenceStep,
                            ParallelJobInstanceId =
                                nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File
                                    ? parallelJobInstanceId
                                    : null,
                            IsConvergenceNextStep = nextWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File &&
                                                    isConvergenceNextStep,
                            TenantId = inputParam.TenantId,
                            ItemInputParams = itemInputParams
                        };

                        if (ct.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                            return GenericResponse<string>.ResultWithError((int)HttpStatusCode.RequestTimeout, null, "Request has been cancelled");
                        }

                        return GenericResponse<string>.ResultWithData(JsonConvert.SerializeObject(output));
                    }

                    return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Can not get list WorkflowStepInfo!");
                }

            }
            catch (Exception ex)
            {

                Log.Logger.Error(ex.StackTrace);
                Log.Logger.Error(ex.Message);
                return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        #endregion

        #region ProcessCheckFinal

        public async Task<GenericResponse<int>> ProcessCheckFinal(JobResult result, string accessToken = null)
        {
            // 2. Process
            Stopwatch sw = new Stopwatch();
            sw.Start();
            GenericResponse<int> response;
            try
            {
                var parse = ObjectId.TryParse(result.JobId, out ObjectId id);
                if (!parse)
                {
                    return GenericResponse<int>.ResultWithData(-1, "Không parse được Id");
                }
                var resultDocItems = new List<StoredDocItem>();
                if (result.IsIgnore == false)
                {
                    resultDocItems = JsonConvert.DeserializeObject<List<StoredDocItem>>(result.Value);

                    if (resultDocItems == null || resultDocItems.Count == 0 || resultDocItems.All(x => string.IsNullOrEmpty(x.Value)))
                    {
                        return GenericResponse<int>.ResultWithData(-1, "Dữ liệu không chính xác");
                    }
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.Eq(x => x.Id, id); // lấy theo id
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)); // ActionCode

                var job = await _repos.FindFirstAsync(filter & filter2 & filter3 & filter4);

                if (job == null)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }

                if (job.DueDate < DateTime.UtcNow)
                {
                    await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời gian");
                    return response;
                }
                var now = DateTime.UtcNow;

                Job resultUpdateJob = null;

                if (result.IsIgnore == false) // nếu không phải bỏ qua phiếu thì thực hiện công việc như thông thường
                {
                    //Validate Value
                    var dbDocItems = JsonConvert.DeserializeObject<List<StoredDocItem>>(job.Value);
                    var isValidCheckFinalValue = IsValidCheckFinalValue(dbDocItems, resultDocItems);
                    if (!isValidCheckFinalValue)
                    {
                        return GenericResponse<int>.ResultWithData(-1, "Dữ liệu không chính xác");
                    }
                    var storedDocItems = new List<StoredDocItem>(dbDocItems);
                    storedDocItems.ForEach(x =>
                    {
                        var resultValue = resultDocItems.FirstOrDefault(_ => _.DocTypeFieldInstanceId == x.DocTypeFieldInstanceId)?.Value;
                        job.HasChange = x.Value != resultValue;
                        x.Value = resultValue;
                    });
                    var updatedValue = JsonConvert.SerializeObject(storedDocItems);
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                    job.Value = updatedValue;
                    job.OldValue = RemoveUnwantedJobOldValue(job.OldValue);
                    job.RightStatus = (short)EnumJob.RightStatus.Correct;
                    job.RightRatio = 1;
                    job.Status = (short)EnumJob.Status.Complete;

                    // Xử lý RightStatus nếu bước SAU là QaCheckFinal
                    var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                    var wfsInfoes = wfInfoes.Item1;
                    var wfSchemaInfoes = wfInfoes.Item2;
                    WorkflowStepInfo crrWfsInfo = null;
                    if (wfsInfoes != null && wfsInfoes.Any())
                    {
                        crrWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == job.WorkflowStepInstanceId.GetValueOrDefault());
                        var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, job.WorkflowStepInstanceId.GetValueOrDefault());
                        if (nextWfsInfoes != null && nextWfsInfoes.Any(x => x.ActionCode == ActionCodeConstants.QACheckFinal))
                        {
                            job.RightStatus = (short)EnumJob.RightStatus.Confirmed;
                            job.RightRatio = 0;     // Chưa xác định được tỷ lệ đúng/sai
                        }
                    }
                    var objConfigPrice = JsonConvert.DeserializeObject<ConfigPriceV2>(crrWfsInfo.ConfigPrice);

                    // Tính toán Price
                    var oldValueDocItems = JsonConvert.DeserializeObject<List<StoredDocItem>>(job.OldValue);
                    decimal price = 0;
                    var priceItems = new List<PriceItem>();
                    if (objConfigPrice != null)
                    {
                        if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                        {
                            // Nếu tồn tại ít nhất 1 trường Edit thì tính theo giá Edit, nếu ko thì tính theo giá Review
                            var isPriceEditTotal = false;
                            foreach (var oldValueItem in oldValueDocItems)
                            {
                                var docItem = storedDocItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == oldValueItem.DocTypeFieldInstanceId);
                                if (docItem != null)
                                {
                                    var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValueItem.Value, docItem?.Value);
                                    if (isPriceEdit)
                                    {
                                        isPriceEditTotal = true;
                                        break;
                                    }
                                }
                            }

                            price = MoneyHelper.GetPriceByConfigPriceV2(crrWfsInfo.ConfigPrice,
                                job.DigitizedTemplateInstanceId, null,
                                isPriceEditTotal);
                            priceItems.Add(new PriceItem
                            {
                                DocTypeFieldInstanceId = job.DocTypeFieldInstanceId,    // job.DocTypeFieldInstanceId = null
                                Price = price,
                                RightStatus = (short)EnumJob.RightStatus.Correct
                            });
                        }
                        else if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                        {
                            foreach (var oldValueItem in oldValueDocItems)
                            {
                                var docItem = storedDocItems
                                    .FirstOrDefault(x => x.DocTypeFieldInstanceId == oldValueItem.DocTypeFieldInstanceId);
                                var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValueItem.Value, docItem?.Value);
                                var itemPrice = MoneyHelper.GetPriceByConfigPriceV2(crrWfsInfo.ConfigPrice,
                                    job.DigitizedTemplateInstanceId, oldValueItem.DocTypeFieldInstanceId,
                                    isPriceEdit);
                                price += itemPrice;
                                priceItems.Add(new PriceItem
                                {
                                    DocTypeFieldInstanceId = docItem.DocTypeFieldInstanceId,
                                    Price = itemPrice,
                                    RightStatus = job.RightStatus
                                });
                            }
                        }
                    }

                    job.Price = price;
                    job.PriceDetails = JsonConvert.SerializeObject(priceItems);

                    resultUpdateJob = await _repos.ReplaceOneAsync(filter2, job);
                }
                else // nếu bỏ qua phiếu
                {
                    job.LastModificationDate = now;
                    job.LastModifiedBy = _userPrincipalService.UserInstanceId;
                    job.Status = (short)EnumJob.Status.Ignore;
                    job.IsIgnore = true;
                    job.ReasonIgnore = result.Comment;
                    job.Price = 0;
                    job.PriceDetails = null;
                    job.RightStatus = (short)EnumJob.RightStatus.Confirmed;

                    resultUpdateJob = await _repos.ReplaceOneAsync(filter2, job);

                    if (resultUpdateJob != null)
                    {
                        response = GenericResponse<int>.ResultWithData(2);
                    }
                    else
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                        }

                        Log.Error($"ProcessCheckFinal fail: job => {job.Code}");
                        response = GenericResponse<int>.ResultWithData(0);
                    }
                }

                if (resultUpdateJob != null)
                {
                    // Trigger after jobs submit with data value; not trigger if job ignore

                    var evt = new AfterProcessCheckFinalEvent
                    {
                        //Job = _mapper.Map<Job, JobDto>(resultUpdateJob),
                        JobId = resultUpdateJob.Id.ToString(),
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<AfterProcessCheckFinalEvent>(evt);

                    // Publish message to DistributionJob to sync job status
                    var logJob = _mapper.Map<Job, LogJobDto>(resultUpdateJob);
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = new List<LogJobDto> { logJob },
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    if (!resultUpdateJob.IsIgnore)
                    {
                        // Update current wfs status is complete
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Complete, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                        }
                    }
                    else
                    {
                        // trường hợp job bị bỏ qua
                        // Update current wfs status is ignore
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Ignore, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                        }
                    }

                    var responseCode = 1; // Ghi nhận thành công
                    if (resultUpdateJob.IsIgnore)
                    {
                        responseCode = 2; //Bỏ qua thành công
                    }
                    response = GenericResponse<int>.ResultWithData(responseCode);
                }
                else
                {
                    throw new Exception($"Lỗi khi ghi dữ liệu - Mã công việc: {job.Code}");
                }

            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError(-1, ex.StackTrace, ex.Message);
                Log.Error($"Error on ProcessCheckFinal => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }
            sw.Stop();
            Log.Debug($"ProcessCheckFinal - Save job result in duration: {sw.ElapsedMilliseconds} ms");
            return response;
        }

        #endregion

        #region ProcessQaCheckFinal

        /// <summary>
        /// Logic: Cho phép QA sửa dữ liệu nếu đánh dấu phiếu PASS / Nếu đánh dấu FALSE thì giữ nguyên dữ liệu của cũ
        /// </summary>
        /// <param name="result"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<GenericResponse<int>> ProcessQaCheckFinal(JobResult result, string accessToken = null)
        {
            // 2. Process
            Stopwatch sw = new Stopwatch();
            sw.Start();
            GenericResponse<int> response;
            try
            {
                var parse = ObjectId.TryParse(result.JobId, out ObjectId id);
                if (!parse)
                {
                    return GenericResponse<int>.ResultWithData(-1, "Không parse được Id");
                }

                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId); // lấy theo người dùng
                var filter2 = Builders<Job>.Filter.Eq(x => x.Id, id); // lấy theo id
                                                                      //var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing); // Lấy các job đang được xử lý
                var filter4 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.QACheckFinal)); // ActionCode

                var job = await _repos.FindFirstAsync(filter & filter2 & filter4);

                if (job == null)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }
                if (job.Status == (short)EnumJob.Status.Ignore)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Công việc này đã bị hủy bỏ trước khi bạn hoàn thành");
                    return response;
                }
                else if (job.Status != (short)EnumJob.Status.Processing)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Công việc đã thay đổi trước khi bạn hoàn thành xử lý");
                    return response;
                }

                if (job.DueDate < DateTime.UtcNow)
                {
                    await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                    response = GenericResponse<int>.ResultWithData(-1, "Hết thời gian");
                    return response;
                }

                var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                var wfsInfoes = wfInfoes.Item1;
                var wfSchemaInfoes = wfInfoes.Item2;
                WorkflowStepInfo crrWfsInfo = null;
                if (wfsInfoes != null && wfsInfoes.Any())
                {
                    crrWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == job.WorkflowStepInstanceId.GetValueOrDefault());
                }
                var objConfigPrice = JsonConvert.DeserializeObject<ConfigPriceV2>(crrWfsInfo.ConfigPrice);

                //var oldValue = job.Value;
                var oldValueDocItem = JsonConvert.DeserializeObject<List<DocItem>>(job.OldValue);

                decimal price = 0;
                var priceItems = new List<PriceItem>();
                string priceDetails = null;

                if (result.QAStatus == true)
                {
                    var resultDocItems = JsonConvert.DeserializeObject<List<StoredDocItem>>(result.Value);

                    if (resultDocItems == null || resultDocItems.Count == 0 || resultDocItems.All(x => string.IsNullOrEmpty(x.Value)))
                    {
                        return GenericResponse<int>.ResultWithData(-1, "Dữ liệu không chính xác");
                    }

                    //Validate Value
                    var dbDocItems = JsonConvert.DeserializeObject<List<StoredDocItem>>(job.Value);
                    var isValidCheckFinalValue = IsValidCheckFinalValue(dbDocItems, resultDocItems);
                    if (!isValidCheckFinalValue)
                    {
                        return GenericResponse<int>.ResultWithData(-1, "Dữ liệu không chính xác");
                    }
                    var storedDocItems = new List<StoredDocItem>(dbDocItems);
                    storedDocItems.ForEach(x =>
                    {
                        var resultValue = resultDocItems.FirstOrDefault(_ => _.DocTypeFieldInstanceId == x.DocTypeFieldInstanceId)?.Value;
                        job.HasChange = x.Value != resultValue;
                        x.Value = resultValue;
                    });
                    var updatedValue = JsonConvert.SerializeObject(storedDocItems);
                    job.Value = updatedValue;
                    job.OldValue = RemoveUnwantedJobOldValue(job.OldValue);

                    // Tính toán Price
                    if (objConfigPrice != null)
                    {
                        if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                        {
                            // Nếu tồn tại ít nhất 1 trường Edit thì tính theo giá Edit, nếu ko thì tính theo giá Review
                            var isPriceEditTotal = false;
                            foreach (var oldValueItem in oldValueDocItem)
                            {
                                var docItem = storedDocItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == oldValueItem.DocTypeFieldInstanceId);
                                if (docItem != null)
                                {
                                    var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValueItem.Value, docItem?.Value);
                                    if (isPriceEdit)
                                    {
                                        isPriceEditTotal = true;
                                        break;
                                    }
                                }
                            }

                            price = MoneyHelper.GetPriceByConfigPriceV2(crrWfsInfo.ConfigPrice,
                                job.DigitizedTemplateInstanceId, null,
                                isPriceEditTotal);
                            priceItems.Add(new PriceItem
                            {
                                DocTypeFieldInstanceId = job.DocTypeFieldInstanceId,    // job.DocTypeFieldInstanceId = null
                                Price = price,
                                RightStatus = (short)EnumJob.RightStatus.Correct
                            });
                        }
                        else if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                        {
                            foreach (var oldValueItem in oldValueDocItem)
                            {
                                var docItem = storedDocItems
                                    .FirstOrDefault(x => x.DocTypeFieldInstanceId == oldValueItem.DocTypeFieldInstanceId);
                                var isPriceEdit = MoneyHelper.IsPriceEdit(job.ActionCode, oldValueItem.Value, docItem?.Value);
                                var itemPrice = MoneyHelper.GetPriceByConfigPriceV2(crrWfsInfo.ConfigPrice,
                                    job.DigitizedTemplateInstanceId, oldValueItem.DocTypeFieldInstanceId,
                                    isPriceEdit);
                                price += itemPrice;
                                priceItems.Add(new PriceItem
                                {
                                    DocTypeFieldInstanceId = docItem.DocTypeFieldInstanceId,
                                    Price = itemPrice,
                                    RightStatus = (short)EnumJob.RightStatus.Correct
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Tính toán Price: Giá được tính là review
                    if (objConfigPrice != null)
                    {
                        if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                        {
                            price = MoneyHelper.GetPriceByConfigPriceV2(
                                crrWfsInfo.ConfigPrice, job.DigitizedTemplateInstanceId, null,
                                false);
                            priceItems.Add(new PriceItem
                            {
                                DocTypeFieldInstanceId = job.DocTypeFieldInstanceId,    // job.DocTypeFieldInstanceId = null
                                Price = price,
                                RightStatus = (short)EnumJob.RightStatus.Correct
                            });
                        }
                        else if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                        {
                            foreach (var itemVal in oldValueDocItem)
                            {
                                price += MoneyHelper.GetPriceByConfigPriceV2(
                                    crrWfsInfo.ConfigPrice, job.DigitizedTemplateInstanceId, itemVal.DocTypeFieldInstanceId,
                                    false);
                                priceItems.Add(new PriceItem
                                {
                                    DocTypeFieldInstanceId = itemVal.DocTypeFieldInstanceId,
                                    Price = price,
                                    RightStatus = (short)EnumJob.RightStatus.Correct
                                });
                            }
                        }
                    }
                }

                job.LastModificationDate = DateTime.UtcNow;
                job.LastModifiedBy = _userPrincipalService.UserInstanceId;

                job.QaStatus = result.QAStatus;

                if (!string.IsNullOrEmpty(result.Comment))
                {
                    job.Note = result.Comment;
                }

                if (priceItems.Any())
                {
                    priceDetails = JsonConvert.SerializeObject(priceItems);
                }
                job.Price = price;
                job.PriceDetails = priceDetails;
                job.RightStatus = (short)EnumJob.RightStatus.Correct;
                job.RightRatio = 1;
                job.Status = (short)EnumJob.Status.Complete;

                var resultUpdateJob = await _repos.ReplaceOneAsync(filter2, job);

                if (resultUpdateJob != null)
                {
                    // Trigger after jobs submit
                    var evt = new AfterProcessQaCheckFinalEvent
                    {
                        //Job = _mapper.Map<Job, JobDto>(resultUpdateJob),
                        JobId = resultUpdateJob.Id.ToString(),
                        AccessToken = accessToken
                    };
                    // Outbox OutboxIntegrationEvent
                    await PublishEvent<AfterProcessQaCheckFinalEvent>(evt);

                    // Publish message sang DistributionJob
                    var logJob = _mapper.Map<Job, LogJobDto>(job);
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = new List<LogJobDto> { logJob },
                        AccessToken = accessToken
                    };

                    // Outbox logjob
                    await PublishEvent<LogJobEvent>(logJobEvt);

                    // Update current wfs status is complete
                    var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Complete, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                    if (!resultDocChangeCurrentWfsInfo.Success)
                    {
                        Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                    }

                    response = GenericResponse<int>.ResultWithData(1);
                }
                else
                {
                    // Update current wfs status is error
                    var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(job.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, job.WorkflowStepInstanceId.GetValueOrDefault(), job.QaStatus, string.IsNullOrEmpty(job.Note) ? string.Empty : job.Note, job.NumOfRound, accessToken: accessToken);
                    if (!resultDocChangeCurrentWfsInfo.Success)
                    {
                        Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} !");
                    }

                    Log.Error($"ProcessQaCheckFinal fail: job => {job.Code}");
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on ProcessQaCheckFinal => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }
            sw.Stop();
            Log.Debug($"ProcessQaCheckFinal - Save job result in duration: {sw.ElapsedMilliseconds} ms");
            return response;
        }

        #endregion

        #region ProcessSyntheticData

        public async Task<GenericResponse<string>> ProcessSyntheticData(ModelInput model, string accessToken = null, CancellationToken ct = default)
        {
            if (model == null || string.IsNullOrEmpty(model.Input))
            {
                return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Bad request!");
            }

            // 2. Process
            while (true)
            {
                // Check at the top of while loop
                if (ct.IsCancellationRequested)
                {
                    // Request has been cancelled
                    ct.ThrowIfCancellationRequested();
                    return null;
                }

                GenericResponse<string> response;
                try
                {
                    var inputParam = JsonConvert.DeserializeObject<InputParam>(model.Input);
                    if (inputParam == null || inputParam.FileInstanceId == null || inputParam.DocInstanceId == null ||
                        string.IsNullOrEmpty(inputParam.Value))
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Error, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), inputParam.QaStatus, string.IsNullOrEmpty(inputParam.Note) ? string.Empty : inputParam.Note, inputParam.NumOfRound, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {inputParam.DocInstanceId.GetValueOrDefault()} !");
                        }

                        return GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, null,
                            "Bad request!");
                    }

                    var docInstanceId = inputParam.DocInstanceId.GetValueOrDefault();
                    var wfsIntanceId = inputParam.WorkflowStepInstanceId.GetValueOrDefault();
                    var actionCode = inputParam.ActionCode;
                    var listDocTypeFieldResponse = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(inputParam.ProjectInstanceId.GetValueOrDefault(), inputParam.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                    if (listDocTypeFieldResponse.Success == false)
                    {
                        throw new Exception("Error call service: _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId");
                    }

                    var ignoreListDocTypeField = listDocTypeFieldResponse.Data.Where(x => x.ShowForInput == false).Select(x => new Nullable<Guid>(x.InstanceId)).ToList();

                    bool hasJobWaitingOrProcessing = await _repository.CheckHasJobWaitingOrProcessingByIgnoreWfs(docInstanceId, actionCode, wfsIntanceId, ignoreListDocTypeField);

                    if (!hasJobWaitingOrProcessing)
                    {
                        // Update FinalValue for Doc
                        var finalValue = inputParam.Value;
                        var docUpdateFinalValueEvt = new DocUpdateFinalValueEvent
                        {
                            DocInstanceId = docInstanceId,
                            FinalValue = finalValue
                        };
                        // Outbox
                        await PublishEvent<DocUpdateFinalValueEvent>(docUpdateFinalValueEvt);


                        // Update all status DocFieldValues is complete
                        var docItems = JsonConvert.DeserializeObject<List<DocItem>>(inputParam.Value);

                        // Cập nhật giá trị value
                        var filter1 = Builders<Job>.Filter.Eq(x => x.FileInstanceId, inputParam.FileInstanceId);
                        var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, inputParam.ActionCode);
                        var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);

                        var storedFinalValue = string.Empty;
                        if (docItems != null && docItems.Any())
                        {
                            var storedDocItems = _mapper.Map<List<DocItem>, List<StoredDocItem>>(docItems);
                            storedFinalValue = JsonConvert.SerializeObject(storedDocItems);
                        }

                        var updateValue = Builders<Job>.Update
                            .Set(s => s.Value, storedFinalValue)
                            .Set(s => s.RightStatus, (short)EnumJob.RightStatus.Correct)
                            .Set(s => s.Status, (short)EnumJob.Status.Complete);

                        var resultUpdateJob = await _repos.UpdateOneAsync(filter1 & filter2 & filter3, updateValue);

                        var wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);
                        var wfSchemaInfoes = JsonConvert.DeserializeObject<List<WorkflowSchemaConditionInfo>>(inputParam.WorkflowSchemaInfoes);

                        if (resultUpdateJob)
                        {
                            // Update current wfs status is complete
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), -1, (short)EnumJob.Status.Complete, inputParam.WorkflowStepInstanceId.GetValueOrDefault(), inputParam.QaStatus, string.IsNullOrEmpty(inputParam.Note) ? string.Empty : inputParam.Note, inputParam.NumOfRound, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {inputParam.DocInstanceId.GetValueOrDefault()} !");
                            }
                            Log.Logger.Information($"ProcessSyntheticData with DocInstanceId: {docInstanceId} success!");
                        }
                        else
                        {
                            // Update current wfs status is error
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(docInstanceId, -1, (short)EnumJob.Status.Error, null, null, string.Empty, null, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {docInstanceId} !");
                            }

                            Log.Logger.Error($"ProcessSyntheticData with DocInstanceId: {inputParam.DocInstanceId} failure!");
                        }

                        if (wfsInfoes != null && wfsInfoes.Any())
                        {
                            var nextWfsInfoes = WorkflowHelper.GetNextSteps(wfsInfoes, wfSchemaInfoes, inputParam.WorkflowStepInstanceId.GetValueOrDefault());
                            if (nextWfsInfoes != null && nextWfsInfoes.Any())
                            {
                                var nextWfsInfo = nextWfsInfoes.First();
                                var output = new InputParam
                                {
                                    FileInstanceId = inputParam.FileInstanceId,
                                    ActionCode = nextWfsInfo.ActionCode,
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
                                    //WorkflowStepInfoes = inputParam.WorkflowStepInfoes,     // Không truyền thông tin này để giảm dung lượng msg
                                    ItemInputParams = new List<ItemInputParam>()
                                };

                                response = GenericResponse<string>.ResultWithData(JsonConvert.SerializeObject(output));

                                if (nextWfsInfo.ActionCode == ActionCodeConstants.End)
                                {
                                    // đây là bước cuối cùng: nextstep = end
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
                                    await _taskRepository.UpdateProgressValue(inputParam.TaskId, updateTaskStepProgress, (short)EnumTask.Status.Complete);

                                    // Update ProjectStatistic
                                    var changeProjectFileProgress = new ProjectFileProgress
                                    {
                                        UnprocessedFile = 0,
                                        ProcessingFile = -1,
                                        CompleteFile = 1,
                                        TotalFile = 0,
                                        ProcessingDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() },
                                        CompleteDocInstanceIds = new List<Guid> { inputParam.DocInstanceId.GetValueOrDefault() }
                                    };
                                    var changeProjectStepProgress = new List<ProjectStepProgress>();
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
                                        ChangeFileProgressStatistic =
                                            JsonConvert.SerializeObject(changeProjectFileProgress),
                                        ChangeStepProgressStatistic =
                                            JsonConvert.SerializeObject(changeProjectStepProgress),
                                        ChangeUserStatistic = string.Empty,
                                        TenantId = inputParam.TenantId
                                    };
                                    await _projectStatisticClientService.UpdateProjectStatisticAsync(changeProjectStatistic, accessToken);

                                    await _moneyService.ChargeMoneyForCompleteDoc(wfsInfoes, wfSchemaInfoes, docItems, docInstanceId, accessToken);
                                }
                            }
                            else
                            {
                                // Update current wfs status is error
                                var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(docInstanceId, -1, (short)EnumJob.Status.Error, null, null, string.Empty, null, accessToken: accessToken);
                                if (!resultDocChangeCurrentWfsInfo.Success)
                                {
                                    Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {docInstanceId} !");
                                }
                                response = GenericResponse<string>.ResultWithData(null, "Can not get next workflowstep!");
                            }
                        }
                        else
                        {
                            // Update current wfs status is error
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(docInstanceId, -1, (short)EnumJob.Status.Error, null, null, string.Empty, null, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {docInstanceId} !");
                            }
                            response = GenericResponse<string>.ResultWithData(null, "Can not get list WorkflowStepInfo!");
                        }
                    }
                    else
                    {
                        // Update current wfs status is error
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeCurrentWorkFlowStepInfo(docInstanceId, -1, (short)EnumJob.Status.Error, null, null, string.Empty, null, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(JobService)}: Error change current work flow step info for DocInstanceId: {docInstanceId} !");
                        }
                        response = GenericResponse<string>.ResultWithData(null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex.StackTrace);
                    Log.Logger.Error(ex.Message);
                    response = GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                }

                // Check at the end of the loop before return statement
                if (ct.IsCancellationRequested)
                {
                    // Request has been cancelled
                    ct.ThrowIfCancellationRequested();
                    return null;
                }

                return response;
            }
        }

        #endregion
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
        /// Convert DocItem -> StoredDocItem để tiết kiệm dung lượng lưu trữ
        /// </summary>
        /// <param name="jobOldValue"></param>
        /// <returns></returns>
        private string RemoveUnwantedJobOldValue(string jobOldValue)
        {
            if (!string.IsNullOrEmpty(jobOldValue))
            {
                var docItems = JsonConvert.DeserializeObject<List<DocItem>>(jobOldValue);
                if (docItems != null && docItems.Any())
                {
                    var storedDocItems = _mapper.Map<List<DocItem>, List<StoredDocItem>>(docItems);
                    return JsonConvert.SerializeObject(storedDocItems);
                }

                return null;
            }

            return null;
        }


    }

    public partial class JobService
    {
        public async Task<GenericResponse<List<Job>>> UpSertMultiJobAsync(List<JobDto> models)
        {
            GenericResponse<List<Job>> response;
            var entities = _mapper.Map<List<JobDto>, List<Job>>(models);
            var result = await _repository.UpSertMultiJobAsync(entities);
            response = GenericResponse<List<Job>>.ResultWithData(result);
            return response;
        }

        public async Task<GenericResponse<List<InfoJob>>> GetInfoJobs(string accessToken = null)
        {
            GenericResponse<List<InfoJob>> response;
            try
            {
                var projectTypeResult = await _projectTypeClientService.GetDropdownExternalAsync(accessToken: accessToken);
                if (!projectTypeResult.Success)
                {
                    return GenericResponse<List<InfoJob>>.ResultWithData(null);
                }
                var projectTypes = projectTypeResult.Data;

                var wfsTypeResult = await _workflowStepTypeClientService.GetAllAsync(accessToken: accessToken);
                if (!wfsTypeResult.Success)
                {
                    return GenericResponse<List<InfoJob>>.ResultWithData(null);
                }
                var wfsTypes = wfsTypeResult.Data.Where(x => !x.IsAuto && x.ActionCode != ActionCodeConstants.Start && x.ActionCode != ActionCodeConstants.End && x.ActionCode != ActionCodeConstants.Upload);

                var result = new List<InfoJob>();
                foreach (var item in projectTypes)
                {
                    var infoJob = new InfoJob
                    {
                        ProjectTypeInstanceId = item.InstanceId,
                        ProjectTypeName = item.Name,
                        InfoJobItems = new List<InfoJobItem>()
                    };

                    var tempWfsTypes = wfsTypes.Where(x => !string.IsNullOrEmpty(x.ProjectTypeInstanceIds) && x.ProjectTypeInstanceIds.Contains(item.InstanceId.ToString())).ToList();
                    if (tempWfsTypes.Any())
                    {
                        foreach (var tempWfsType in tempWfsTypes)
                        {
                            // bool hasJob = await _repository.CheckHasJobByProjectTypeActionCode(item.InstanceId, tempWfsType.ActionCode);
                            infoJob.InfoJobItems.Add(new InfoJobItem
                            {
                                WorkflowStepName = tempWfsType.Name,
                                //HasJob = hasJob,
                                ServiceCode = tempWfsType.ServiceCode,
                                ApiEndpoint = tempWfsType.ApiEndpoint,
                                HttpMethodType = tempWfsType.HttpMethodType,
                                ViewUrl = tempWfsType.ViewUrl,
                                Icon = tempWfsType.Icon,
                                ActionCode = tempWfsType.ActionCode
                            });
                        }
                    }
                    result.Add(infoJob);
                }
                response = GenericResponse<List<InfoJob>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<InfoJob>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<JobDto>> GetProcessingJobById(string id)
        {
            GenericResponse<JobDto> response;
            if (!ObjectId.TryParse(id, out ObjectId jobId))
            {
                response = GenericResponse<JobDto>.ResultWithData(null, "Mã công việc không chính xác");
                return response;
            }

            var filterId = Builders<Job>.Filter.Eq(x => x.Id, jobId);
            var filterUser = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId);
            var filterStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);// Lấy các job đang được xử lý

            var job = await _repository.FindFirstAsync(filterId & filterUser & filterStatus);
            var result = _mapper.Map<Job, JobDto>(job);
            response = GenericResponse<JobDto>.ResultWithData(result);
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByDocInstanceId(Guid docInstanceId)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId) & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.Ocr));
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByDocInstanceIds(List<Guid> docInstanceIds)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var lstDocInstanceIds = docInstanceIds.Select(x => (Guid?)x).ToList();
                var filter = Builders<Job>.Filter.In(x => x.DocInstanceId, lstDocInstanceIds) & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.Ocr));
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<JobDto>> GetProcessingJobCheckFinalByFileInstanceId(Guid fileInstanceId)
        {
            GenericResponse<JobDto> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.FileInstanceId, fileInstanceId) & Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
                var data = await _repos.FindFirstAsync(filter);
                var dataDto = _mapper.Map<Job, JobDto>(data);
                response = GenericResponse<JobDto>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<JobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<JobDto>> GetProcessingJobQACheckFinalByFileInstanceId(Guid fileInstanceId)
        {
            GenericResponse<JobDto> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.FileInstanceId, fileInstanceId) & Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.QACheckFinal)) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
                var data = await _repos.FindFirstAsync(filter);
                var dataDto = _mapper.Map<Job, JobDto>(data);
                response = GenericResponse<JobDto>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<JobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        /// <summary>
        /// Lấy danh sách các job đang xử lý dở dang bởi worker ID
        /// </summary>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<GenericResponse<List<JobDto>>> GetListJob(string actionCode = null, string accessToken = null)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId);
                var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode); // ActionCode
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);// Lấy các job đang được xử lý

                var data = await _repos.FindAsync(filter & filter2 & filter3);

                data = await AddDocTypeFieldExtraSetting(data, actionCode, accessToken);

                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);

                if (data.Exists(x => x.DueDate < DateTime.UtcNow))
                {
                    var userId_turnIDHashSet = new HashSet<string>();
                    foreach (var job in data)
                    {
                        //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                        var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                        if (!userId_turnIDHashSet.Contains(hashkey))
                        {
                            userId_turnIDHashSet.Add(hashkey);
                            await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                        }
                    }

                    response = GenericResponse<List<JobDto>>.ResultWithData(new List<JobDto>());
                    return response;
                }

                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> DistributeJobCheckFinalBouncedToNewUser(Guid projectInstanceId, string path, Guid userInstanceId, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter1 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)); //Chỉ lấy job CheckFinal
                var filter2 = Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{path}"));// lấy các job có DocPath bắt đầu bằng path
                var filter3 = Builders<Job>.Filter.Ne(x => x.Status, (short)EnumJob.Status.Complete);// Lấy các job có trạng thái khác Complete
                var filter4 = Builders<Job>.Filter.Gt(x => x.NumOfRound, 0); //NumOfRound lớn hơn 0
                var data = await _repos.FindAsync(filter & filter1 & filter2 & filter3 & filter4);

                int countSuccess = 0;
                int countFail = 0;
                if (data != null && data.Count() > 0)
                {
                    foreach (var job in data)
                    {
                        var cloneJob = (Job)job.Clone();
                        var currentLastModifyBy = job.LastModifiedBy;
                        job.LastModifiedBy = userInstanceId;
                        var filterJobById = Builders<Job>.Filter.Eq(x => x.Id, job.Id); // lấy theo id
                        var updateLastUser = Builders<Job>.Update
                       .Set(s => s.LastModifiedBy, userInstanceId);
                        var kq = await _repos.UpdateOneAsync(filterJobById, updateLastUser);
                        if (kq)
                        {
                            // Publish message sang DistributionJob
                            var logJob = _mapper.Map<Job, LogJobDto>(job);
                            var logJobEvt = new LogJobEvent
                            {
                                LogJobs = new List<LogJobDto> { logJob },
                                AccessToken = accessToken
                            };

                            await PublishEvent<LogJobEvent>(logJobEvt);
                            countSuccess++;
                        }
                        else
                        {
                            Log.Error($"Can not assigned field LastModifiedBy to job: {job.Code}");
                            countFail++;
                        }
                    }
                }
                if (countSuccess <= 0)
                {
                    response = GenericResponse<int>.ResultWithError(1, $"Phân phối việc không thành công, lỗi {countFail} file");
                }
                else response = GenericResponse<int>.ResultWithData(1, $"Phân phối thành công {countSuccess} file, lỗi {countFail} file");

            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<CountJobDto>> GetListJobCheckFinalByPath(Guid projectInstanceId, string path)
        {
            GenericResponse<CountJobDto> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                filter &= Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)); //Chỉ lấy job CheckFinal
                filter &= Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{path}"));// lấy các job có DocPath bắt đầu bằng path
                var filter3 = Builders<Job>.Filter.Ne(x => x.Status, (short)EnumJob.Status.Complete);// Lấy các job có trạng thái khác Complete
                var filter4 = Builders<Job>.Filter.Gt(x => x.NumOfRound, 0); //NumOfRound lớn hơn 0s
                var filter5 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Ignore);// Lấy các job có trạng thái Ignore
                var bounced = await _repos.CountAsync(filter & filter3 & filter4);
                var ignore = await _repos.CountAsync(filter & filter5);
                var result = new CountJobDto();
                result.CountIgnore = ignore > 0 ? ignore : 0;
                result.CountBounced = bounced > 0 ? bounced : 0;
                response = GenericResponse<CountJobDto>.ResultWithData(result);

            }
            catch (Exception ex)
            {
                response = GenericResponse<CountJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetProactiveListJob(string actionCode = null, Guid? projectTypeInstanceId = null, string accessToken = null)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                if (_userPrincipalService.UserInstanceId != null && _userPrincipalService.UserInstanceId != Guid.Empty)
                {
                    //// 31/12/2021 BA: Không phân biệt user thuộc nhóm quyền nào, ai cũng có quyền nhận việc
                    //// Check xem user có phải là role DataProcessing
                    //var checkExistByRoleCodeResult = await _appUserClientService.CheckExistByRoleCodeAsync(
                    //    _userPrincipalService.UserInstanceId.GetValueOrDefault(), DataProcessing, accessToken);
                    //if (!checkExistByRoleCodeResult.Success || checkExistByRoleCodeResult.Data == false)
                    //{
                    //    return GenericResponse<List<JobDto>>.ResultWithData(new List<JobDto>());
                    //}

                    // Check xem user hiện có đang tồn đọng công việc
                    var oldJobs = await _repository.GetJobProcessingByProjectAsync(_userPrincipalService.UserInstanceId.GetValueOrDefault(), actionCode, projectTypeInstanceId.GetValueOrDefault());
                    if (oldJobs.Count > 0)
                    {
                        var result = _mapper.Map<List<Job>, List<JobDto>>(oldJobs);
                        return GenericResponse<List<JobDto>>.ResultWithData(result, "Người dùng hiện đang còn tồn đọng công việc!");
                    }

                    var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, null);
                    var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode); // ActionCode
                    var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);// Lấy các job đang đợi phân công
                    var filter = filter1 & filter2 & filter3;
                    if (projectTypeInstanceId != null && projectTypeInstanceId != Guid.Empty)
                    {
                        filter = filter & Builders<Job>.Filter.Eq(x => x.ProjectTypeInstanceId, projectTypeInstanceId);
                    }
                    var lstProjectStore = await _repository.GetDistinctProjectOrderByCreatedDate(filter);
                    if (lstProjectStore == null || lstProjectStore.Count == 0)
                    {
                        Log.Information("Can't get list ProjectStore because there is not any jobs!");
                        return GenericResponse<List<JobDto>>.ResultWithData(new List<JobDto>());
                    }

                    var jobDtos = new List<JobDto>();
                    foreach (var prjStore in lstProjectStore)
                    {
                        if (projectTypeInstanceId == null)
                        {
                            projectTypeInstanceId = prjStore.ProjectTypeInstanceId;
                        }
                        var projectInstanceId = prjStore.ProjectInstanceId;
                        var workflowInstanceId = prjStore.WorkflowInstanceId;
                        var tenantId = prjStore.TenantId;
                        var filter4 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);// Lấy các job theo projectInstanceId

                        var jobs = new List<Job>();
                        var turnInstanceId = Guid.NewGuid();

                        var wfInfoes = await GetWfInfoes(workflowInstanceId.GetValueOrDefault(), accessToken);
                        var wfsInfoes = wfInfoes.Item1;
                        var wfSchemaInfoes = wfInfoes.Item2;

                        if (wfsInfoes != null && wfsInfoes.Any())
                        {
                            foreach (var wfsInfo in wfsInfoes.Where(x => x.ActionCode == actionCode))
                            {
                                if (string.IsNullOrEmpty(wfsInfo.ConfigStep))
                                {
                                    continue;
                                }

                                var configStep = JObject.Parse(wfsInfo.ConfigStep);

                                // check user is processing in wfsConfig
                                bool isChooseProcessingUser =
                                    configStep[ConfigStepPropertyConstants.IsChooseProcessingUser] == null || (bool)configStep[ConfigStepPropertyConstants.IsChooseProcessingUser];
                                if (isChooseProcessingUser)
                                {
                                    if (configStep[ConfigStepPropertyConstants.ConfigStepUsers] == null)
                                    {
                                        continue;
                                    }

                                    var configStepUsers =
                                        JsonConvert.DeserializeObject<List<ConfigStepUser>>(configStep[ConfigStepPropertyConstants.ConfigStepUsers].ToString());
                                    if (!configStepUsers.Any(x => x.UserInstanceId == _userPrincipalService.UserInstanceId && x.Status == (short)EnumConfigStepUser.Status.Processing))
                                    {
                                        continue;
                                    }
                                }

                                int pageSize = configStep[ConfigStepPropertyConstants.NumOfJobDistributed] != null
                                    ? Int32.Parse(configStep[ConfigStepPropertyConstants.NumOfJobDistributed].ToString())
                                    : 10;

                                var filterWfs = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wfsInfo.InstanceId);// Lấy các job theo workflowStepInstanceId tương ứng
                                var sortDefinition = Builders<Job>.Sort.Ascending(nameof(Job.CreatedDate)).Ascending(nameof(Job.LastModificationDate));

                                jobs = await _repository.GetAllJobAsync(filter & filter4 & filterWfs, sortDefinition, pageSize);
                                if (jobs.Any())
                                {
                                    var now = DateTime.UtcNow;
                                    Int32.TryParse(configStep[ConfigStepPropertyConstants.MaxTimeProcessing].ToString(),
                                        out var maxTimeProcessing);
                                    foreach (var job in jobs)
                                    {
                                        job.UserInstanceId = _userPrincipalService.UserInstanceId;
                                        job.TurnInstanceId = turnInstanceId;
                                        job.ReceivedDate = now;
                                        job.DueDate = now.AddMinutes(maxTimeProcessing);
                                        job.Status = (short)EnumJob.Status.Processing;
                                        job.LastModificationDate = now;
                                    }

                                    var resultUpdateJob = await _repos.UpdateMultiAsync(jobs);

                                    if (resultUpdateJob > 0)
                                    {
                                        // TaskStepProgress: Update value
                                        var sortOrder = WorkflowHelper.GetOrderStep(wfsInfoes, wfsInfo.InstanceId);
                                        foreach (var job in jobs)
                                        {
                                            var updatedTaskStepProgress = new TaskStepProgress
                                            {
                                                Id = wfsInfo.Id,
                                                InstanceId = wfsInfo.InstanceId,
                                                Name = wfsInfo.Name,
                                                ActionCode = wfsInfo.ActionCode,
                                                WaitingJob = -1,
                                                ProcessingJob = 1,
                                                CompleteJob = 0,
                                                TotalJob = 0,
                                                Status = (short)EnumTaskStepProgress.Status.Processing
                                            };
                                            var taskResult = await _taskRepository.UpdateProgressValue(job.TaskId.ToString(), updatedTaskStepProgress);
                                            if (taskResult != null)
                                            {
                                                Log.Logger.Information($"TaskStepProgress: +1 ProcessingJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} success!");
                                            }
                                            else
                                            {
                                                Log.Logger.Error($"TaskStepProgress: +1 ProcessingJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} failure!");
                                            }
                                        }

                                        // 2.1. Mark doc, task processing & update ProjectStatistic
                                        var crrWfsInfo = wfsInfo;
                                        var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
                                        var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct();
                                        if (WorkflowHelper.IsMarkDocProcessing(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId))
                                        {
                                            // Mark doc processing
                                            if (docInstanceIds.Any())
                                            {
                                                string strDocInstanceIds = JsonConvert.SerializeObject(docInstanceIds);
                                                await _docClientService.ChangeStatusMulti(strDocInstanceIds, accessToken: accessToken);
                                            }

                                            // Mark task processing
                                            var taskIds = jobs.Where(x => x.TaskId != ObjectId.Empty).Select(x => x.TaskId.ToString()).Distinct().ToList();
                                            if (taskIds.Any())
                                            {
                                                var taskInstanceIds = jobs.Select(x => x.TaskInstanceId).Distinct().ToList();
                                                var taskResults = await _taskRepository.ChangeStatusMulti(taskIds);
                                                string msgProcessingTasks = taskInstanceIds.Count == 1 ? "ProcessingTask" : "ProcessingTasks";
                                                string msgTaskInstanceIds = taskInstanceIds.Count == 1 ? "TaskInstanceId" : "TaskInstanceIds";
                                                if (taskResults)
                                                {
                                                    Log.Logger.Information($"TaskStepProgress: +{taskInstanceIds.Count} {msgProcessingTasks} in {msgTaskInstanceIds}: {string.Join(',', taskInstanceIds)} success!");
                                                }
                                                else
                                                {
                                                    Log.Logger.Error($"TaskStepProgress: +{taskInstanceIds.Count} {msgProcessingTasks} in {msgTaskInstanceIds}: {string.Join(',', taskInstanceIds)} failure!");
                                                }
                                            }
                                        }

                                        // ProjectStatistic: Update
                                        if (docInstanceIds.Any())
                                        {
                                            var changeProjectStatisticMulti = new ProjectStatisticUpdateMultiProgressDto
                                            {
                                                ItemProjectStatisticUpdateProgresses = new List<ItemProjectStatisticUpdateProgressDto>(),
                                                ProjectTypeInstanceId = projectTypeInstanceId,
                                                ProjectInstanceId = projectInstanceId,
                                                WorkflowInstanceId = workflowInstanceId,
                                                WorkflowStepInstanceId = wfsInfo.InstanceId,
                                                ActionCode = actionCode,
                                                DocInstanceIds = JsonConvert.SerializeObject(docInstanceIds),
                                                TenantId = jobs.First().TenantId
                                            };

                                            int countOfProcessingFileInFileStatistic = 0;
                                            var processingDocInstanceIdsInFileStatistic = new List<Guid>();
                                            int countOfProcessingFileInStepStatistic = 0;
                                            var processingDocInstanceIdsStepStatistic = new List<Guid>();
                                            foreach (var docInstanceId in docInstanceIds)
                                            {
                                                ProjectFileProgress changeProjectFileProgress;
                                                if (prevWfsInfoes.Count == 1 && prevWfsInfoes.FirstOrDefault(x => x.ActionCode == ActionCodeConstants.Upload) != null)
                                                {
                                                    changeProjectFileProgress = new ProjectFileProgress
                                                    {
                                                        UnprocessedFile = -1,
                                                        ProcessingFile = 1,
                                                        CompleteFile = 0,
                                                        TotalFile = 0,
                                                        UnprocessedDocInstanceIds = new List<Guid> { docInstanceId },
                                                        ProcessingDocInstanceIds = new List<Guid> { docInstanceId }
                                                    };
                                                    countOfProcessingFileInFileStatistic++;
                                                    processingDocInstanceIdsInFileStatistic.Add(docInstanceId);
                                                }
                                                else
                                                {
                                                    changeProjectFileProgress = new ProjectFileProgress
                                                    {
                                                        UnprocessedFile = 0,
                                                        ProcessingFile = 0,
                                                        CompleteFile = 0,
                                                        TotalFile = 0
                                                    };
                                                }

                                                var crrJobs = jobs.Where(x => x.DocInstanceId == docInstanceId).ToList();
                                                var changeProjectStepProgress = new List<ProjectStepProgress>();
                                                // Nếu tồn tại job Complete thì ko chuyển trạng thái về Processing
                                                var hasJobComplete =
                                                    await _repository.CheckHasJobCompleteByWfs(docInstanceId, actionCode, crrWfsInfo.InstanceId);
                                                if (!hasJobComplete)
                                                {
                                                    changeProjectStepProgress = crrJobs.GroupBy(x => new { x.ProjectInstanceId, x.WorkflowInstanceId, x.WorkflowStepInstanceId, x.ActionCode }).Select(grp => new ProjectStepProgress
                                                    {
                                                        InstanceId = grp.Key.WorkflowStepInstanceId.GetValueOrDefault(),
                                                        Name = string.Empty,
                                                        ActionCode = grp.Key.ActionCode,
                                                        ProcessingFile = grp.Select(i => i.DocInstanceId.GetValueOrDefault()).Distinct().Count(),
                                                        CompleteFile = 0,
                                                        TotalFile = 0,
                                                        ProcessingDocInstanceIds = new List<Guid> { docInstanceId }
                                                    }).ToList();
                                                    countOfProcessingFileInStepStatistic +=
                                                        changeProjectStepProgress.Sum(s => s.ProcessingFile);
                                                    processingDocInstanceIdsStepStatistic.Add(docInstanceId);
                                                }

                                                changeProjectStatisticMulti.ItemProjectStatisticUpdateProgresses.Add(new ItemProjectStatisticUpdateProgressDto
                                                {
                                                    DocInstanceId = docInstanceId,
                                                    StatisticDate = Int32.Parse(crrJobs.First().DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                                                    ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                                                    ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                                                    ChangeUserStatistic = string.Empty  // Worker phải hoàn thành công việc thì mới đc thống kê vào dự án
                                                });
                                            }

                                            if (countOfProcessingFileInFileStatistic > 0 || countOfProcessingFileInStepStatistic > 0)
                                            {
                                                await _projectStatisticClientService.UpdateMultiProjectStatisticAsync(changeProjectStatisticMulti, accessToken);

                                                string msgProcessingFilesInFileStatistic = countOfProcessingFileInFileStatistic == 1 ? "ProcessingFile" : "ProcessingFiles";
                                                string msgDocInstanceIdsInFileStatistic = countOfProcessingFileInFileStatistic == 1 ? "DocInstanceId" : "DocInstanceIds";
                                                string msgTaskInstanceIdsInStepStatistic = countOfProcessingFileInStepStatistic == 1 ? "ProcessingFile" : "ProcessingFiles";
                                                string msgDocInstanceIdsInStepStatistic = countOfProcessingFileInStepStatistic == 1 ? "DocInstanceId" : "DocInstanceIds";
                                                string msgFileStatistic = countOfProcessingFileInFileStatistic > 0
                                                    ? $"+{countOfProcessingFileInFileStatistic} {msgProcessingFilesInFileStatistic} for FileProgressStatistic with {msgDocInstanceIdsInFileStatistic}: {string.Join(',', processingDocInstanceIdsInFileStatistic)}, "
                                                    : "";
                                                string msgStepStatistic = countOfProcessingFileInStepStatistic > 0
                                                    ? $"+{countOfProcessingFileInStepStatistic} {msgTaskInstanceIdsInStepStatistic} for StepProgressStatistic with {msgDocInstanceIdsInStepStatistic}: {string.Join(',', processingDocInstanceIdsStepStatistic)}"
                                                    : "";
                                                string message = $"Published {nameof(ProjectStatisticUpdateMultiProgressEvent)}: ProjectStatistic: {msgFileStatistic}{msgStepStatistic}";
                                                Log.Logger.Information(message);
                                            }
                                        }
                                    }

                                    //SetCacheRedis
                                    await SetCacheRecall(_userPrincipalService.UserInstanceId.Value, turnInstanceId, maxTimeProcessing, accessToken);

                                    break;
                                }
                            }
                        }

                        if (jobs.Count > 0)
                        {
                            jobDtos = _mapper.Map<List<Job>, List<JobDto>>(jobs);
                            break;
                        }
                    }
                    if (jobDtos.Any())
                    {
                        var docInstanceIds = jobDtos.Select(x => x.DocInstanceId).Distinct().ToList();
                        // Update current wfs status is processing
                        var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(docInstanceIds), -1, (short)EnumJob.Status.Processing, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfo.Success)
                        {
                            Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change multi current work flow step info for DocInstanceIds {JsonConvert.SerializeObject(docInstanceIds)} !");
                        }
                    }
                    response = GenericResponse<List<JobDto>>.ResultWithData(jobDtos);
                }
                else
                {
                    response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized.ToString(), "Chưa đăng nhập");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Lỗi nhận việc => {ex.Message} => {ex.StackTrace}");
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<bool>> CheckUserHasJob(Guid userInstanceId, Guid projectInstanceId, string actionCode = null, short status = (short)EnumJob.Status.Processing)
        {
            if (userInstanceId == Guid.Empty || projectInstanceId == Guid.Empty)
            {
                return GenericResponse<bool>.ResultWithData(false);
            }

            GenericResponse<bool> response;
            try
            {
                var rs = await _repository.CheckUserHasJob(userInstanceId, projectInstanceId, actionCode, status);
                response = GenericResponse<bool>.ResultWithData(rs);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<bool>> CheckHasJobWaitingOrProcessingByMultiWfs(DocCheckHasJobWaitingOrProcessingDto model)
        {
            if (model == null || model.DocInstanceId == Guid.Empty || !model.CheckWorkflowStepInfos.Any())
            {
                return GenericResponse<bool>.ResultWithData(false);
            }

            GenericResponse<bool> response;
            try
            {
                var rs = await _repository.CheckHasJobWaitingOrProcessingByMultiWfs(model.DocInstanceId, model.CheckWorkflowStepInfos, model.IgnoreListDocTypeField);
                response = GenericResponse<bool>.ResultWithData(rs);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<bool>> CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId,
            Guid? parallelJobInstanceId)
        {
            if (docInstanceId == Guid.Empty)
            {
                return GenericResponse<bool>.ResultWithData(false);
            }

            GenericResponse<bool> response;
            try
            {
                var rs = await _repository.CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(docInstanceId, docFieldValueInstanceId, parallelJobInstanceId);
                response = GenericResponse<bool>.ResultWithData(rs);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetJobCompleteByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId,
            Guid? parallelJobInstanceId)
        {
            if (docInstanceId == Guid.Empty)
            {
                return GenericResponse<List<JobDto>>.ResultWithData(null);
            }

            GenericResponse<List<JobDto>> response;
            try
            {
                var rs = await _repository.GetJobCompleteByDocFieldValueAndParallelJob(docInstanceId, docFieldValueInstanceId, parallelJobInstanceId);
                response = GenericResponse<List<JobDto>>.ResultWithData(_mapper.Map<List<Job>, List<JobDto>>(rs));
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetJobByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null,
            short? status = null)
        {
            if (docInstanceId == Guid.Empty)
            {
                return GenericResponse<List<JobDto>>.ResultWithData(null);
            }

            GenericResponse<List<JobDto>> response;
            try
            {
                var data = await _repository.GetJobByWfs(docInstanceId, actionCode, workflowStepInstanceId, status);
                var result = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetJobByWfsInstanceIds(Guid docInstanceId, string workflowStepInstanceIds)
        {
            if (docInstanceId == Guid.Empty)
            {
                return GenericResponse<List<JobDto>>.ResultWithData(null);
            }

            GenericResponse<List<JobDto>> response;
            try
            {
                var lstWfsIntanceIds = JsonConvert.DeserializeObject<List<Guid>>(workflowStepInstanceIds);
                var data = await _repository.GetJobByWfsInstanceIds(docInstanceId, lstWfsIntanceIds);
                var result = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<IEnumerable<SelectItemDto>>> GetDropDownFileCheckFinal(string accessToken)
        {
            if (_userPrincipalService == null || _userPrincipalService.UserInstanceId == Guid.Empty)
            {
                return GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa đăng nhập", "Chưa đăng nhập");
            }

            var userInstanceId = _userPrincipalService.UserInstanceId.GetValueOrDefault();
            var projectDefaultResponse = await _userConfigClientService.GetValueByCodeAsync(UserConfigCodeConstants.Project,
                            accessToken);

            if (!projectDefaultResponse.Success || projectDefaultResponse.Data == null || string.IsNullOrEmpty(projectDefaultResponse.Data))
            {
                Log.Information("Không lấy được dự án");
                return GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
            }
            var projectDefault = JsonConvert.DeserializeObject<ProjectCache>(projectDefaultResponse.Data);
            if (projectDefault == null)
            {
                Log.Information("Không lấy được dự án");
                return GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
            }
            var projectInstanceId = projectDefault.InstanceId;

            GenericResponse<IEnumerable<SelectItemDto>> response;
            try
            {
                var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
                var filterProject = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal));


                var resultJob = (await _repository.FindAsync(filter1 & filter2 & filterProject));
                if (resultJob == null)
                {
                    return GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.NoContent, "Không có dữ liệu", "Không có dữ liệu");
                }

                //Check fullpart
                var lstFileNotComplete = resultJob.Where(x => x.Status != (short)EnumJob.Status.Processing).Select(x => x.FileInstanceId).Distinct().ToList();
                var lstFile = resultJob.Where(x => !lstFileNotComplete.Contains(x.FileInstanceId)).GroupBy(x => x.FileInstanceId).Select(grp => new
                {
                    FileInstanceId = grp.Key,
                    FileName = grp.Select(g => g.DocName).FirstOrDefault()
                }).Select(n => new SelectItemDto(n.FileInstanceId.Value, n.FileName)).AsEnumerable();
                response = GenericResponse<IEnumerable<SelectItemDto>>.ResultWithData(lstFile);
            }
            catch (Exception ex)
            {
                response = GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<long>> ReCallJobByUser(string accessToken = null)
        {
            var userInstanceId = _userPrincipalService.UserInstanceId.GetValueOrDefault();
            var fitlerUser = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
            var fitlerStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            var fitlerDudeDate = Builders<Job>.Filter.Lt(x => x.DueDate, DateTime.UtcNow);

            // Cập nhật LastModificationDate để đẩy thứ tự ưu tiên của những việc thu hồi lên
            var jobs = await _repository.FindAsync(fitlerUser & fitlerStatus & fitlerDudeDate);
            var userId_turnIDHashSet = new HashSet<string>();
            foreach (var job in jobs)
            {
                //lọc ra các cặp khóa UserID và TurnID để xử lý Recall Job
                var hashkey = string.Format("{0}_{1}", job.UserInstanceId.ToString(), job.TurnInstanceId.ToString());
                if (!userId_turnIDHashSet.Contains(hashkey))
                {
                    userId_turnIDHashSet.Add(hashkey);
                    await _recallJobWorkerService.RecallJobByTurn(job.UserInstanceId.GetValueOrDefault(), job.TurnInstanceId.GetValueOrDefault(), accessToken);
                }
            }

            return GenericResponse<long>.ResultWithData(jobs.Count);
        }

        public async Task<GenericResponse<int>> SkipJobDataEntry(string jobIdstr, string reason)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdstr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");
                return response;
            }

            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);
            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");
                return response;
            }

            if (job.IsIgnore)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đã bỏ qua");
                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.DataEntry))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc nhập liệu");
                return response;
            }
            //var filterNextStep = Builders<Job>.Filter.Eq(x => x.Id, job.NextStepId); // lấy theo id

            var updateSkip = Builders<Job>.Update
               //.Set(s => s.Status, (short)(EnumJob.Status.Complete))
               .Set(s => s.ReasonIgnore, reason)
               .Set(s => s.LastModificationDate, DateTime.UtcNow)
               .Set(s => s.IsIgnore, true)
               .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var updateNextStep = Builders<Job>.Update
               //.Set(s => s.Status, (short)(EnumJob.Status.Waiting))
               .Set(s => s.IsIgnore, true)
               .Set(s => s.ReasonIgnore, reason);


            var rs = await _repository.UpdateOneAsync(filter, updateSkip);
            //var rsNextStep = await _repository.UpdateOneAsync(filterNextStep, updateNextStep);

            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<GenericResponse<int>> UndoSkipJobDataEntry(string jobIdStr)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdStr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");
                return response;
            }
            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);
            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");
                return response;
            }

            if (!job.IsIgnore)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc chưa bỏ qua");
                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.DataEntry))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc nhập liệu");
                return response;
            }

            //var filterNextStep = Builders<Job>.Filter.Eq(x => x.Id, job.NextStepId); // lấy theo id

            var updateSkip = Builders<Job>.Update
               //.Set(s => s.Status, (short)(EnumJob.Status.Complete))
               .Set(s => s.ReasonIgnore, null)
               .Set(s => s.LastModificationDate, DateTime.UtcNow)
               .Set(s => s.IsIgnore, false)
               .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var updateNextStep = Builders<Job>.Update
               //.Set(s => s.Status, (short)(EnumJob.Status.Waiting))
               .Set(s => s.IsIgnore, false)
               .Set(s => s.ReasonIgnore, null);

            var rs = await _repository.UpdateOneAsync(filter, updateSkip);

            //var rsNextStep = await _repos.UpdateOneAsync(filterNextStep, updateNextStep);

            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<GenericResponse<long>> GetCountJobWaiting(string actionCode, string accessToken)
        {
            GenericResponse<long> response;
            try
            {
                var filter1 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
                var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                var filter3 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, null);
                //var countOfJobs = await _repos.CountAsync(filter1 & filter2 & filter3);
                //response = GenericResponse<long>.ResultWithData(countOfJobs);

                var wfsInstanceIds = await _repository.GetDistinctWfsInstanceId(filter1 & filter2 & filter3);
                if (wfsInstanceIds.Any())
                {
                    long countOfJobs = 0;
                    var resultConfigSteps = await _workflowStepClientService.GetConfigStepByInstanceIdsAsync(JsonConvert.SerializeObject(wfsInstanceIds), accessToken);
                    if (resultConfigSteps != null && resultConfigSteps.Success && resultConfigSteps.Data.Any())
                    {
                        var userInstanceId = _userPrincipalService.UserInstanceId.GetValueOrDefault();
                        var dicConfigSteps = resultConfigSteps.Data.Select(kpv => kpv.Value);
                        var configSteps = dicConfigSteps.Select(x => JObject.Parse(x));
                        foreach (var configStep in configSteps)
                        {
                            if (configStep.ContainsKey(ConfigStepPropertyConstants.IsChooseProcessingUser) &&
                                configStep[ConfigStepPropertyConstants.IsChooseProcessingUser].Type is JTokenType.Boolean)
                            {
                                if (Boolean.TryParse(configStep[ConfigStepPropertyConstants.IsChooseProcessingUser].ToString(), out bool isChooseProcessingUser))
                                {
                                    if (!isChooseProcessingUser)
                                    {
                                        countOfJobs++;
                                        break;
                                    }
                                    else
                                    {
                                        if (configStep.ContainsKey(ConfigStepPropertyConstants.ConfigStepUsers) &&
                                            configStep[ConfigStepPropertyConstants.ConfigStepUsers].Type is JTokenType.String)
                                        {
                                            var configStepUsers =
                                                JsonConvert.DeserializeObject<List<ConfigStepUser>>((string)configStep[ConfigStepPropertyConstants.ConfigStepUsers]);
                                            var existed = configStepUsers.Any(x =>
                                                x.UserInstanceId == userInstanceId &&
                                                x.Status == (short)EnumConfigStepUser.Status.Processing);
                                            if (existed)
                                            {
                                                countOfJobs++;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    response = GenericResponse<long>.ResultWithData(countOfJobs);
                }
                else
                {
                    response = GenericResponse<long>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithData(0, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> WarningCheckFinal(string jobIdstr, string reason)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdstr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");

                return response;
            }

            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);
            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");

                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.CheckFinal))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc xác nhận");
                return response;
            }

            var updateSkip = Builders<Job>.Update
               //.Set(s => s.Status, (short)(EnumJob.Status.Complete))
               .Set(s => s.ReasonWarning, reason)
               .Set(s => s.LastModificationDate, DateTime.UtcNow)
               .Set(s => s.IsWarning, true)
               .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var rs = await _repository.UpdateOneAsync(filter, updateSkip);

            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<GenericResponse<int>> UndoWarningCheckFinal(string jobIdStr)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdStr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");

                return response;
            }
            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);

            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");

                return response;
            }

            if (!job.IsWarning)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc chưa bỏ qua");

                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.CheckFinal))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc xác nhận");
                return response;
            }

            var updateSkip = Builders<Job>.Update
              //.Set(s => s.Status, (short)(EnumJob.Status.Complete))
              .Set(s => s.ReasonWarning, null)
              .Set(s => s.LastModificationDate, DateTime.UtcNow)
              .Set(s => s.IsWarning, false)
              .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var rs = await _repository.UpdateOneAsync(filter, updateSkip);


            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<GenericResponse<bool>> UpdateValueJob(UpdateValueJob model)
        {
            GenericResponse<bool> response;
            try
            {
                var filter1 = Builders<Job>.Filter.Eq(x => x.FileInstanceId, model.FileInstanceId);
                var filter2 = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, model.WorkflowStepInstanceId);
                var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, model.ActionCode);
                var filter4 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);

                var updateValue = Builders<Job>.Update
                    .Set(s => s.Value, model.Value)
                    .Set(s => s.RightStatus, (short)EnumJob.RightStatus.Correct)
                    .Set(s => s.LastModificationDate, DateTime.UtcNow)
                    .Set(s => s.Status, (short)EnumJob.Status.Complete);

                var result = await _repos.UpdateOneAsync(filter1 & filter2 & filter3 & filter4, updateValue);
                response = GenericResponse<bool>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<bool>> UpdateMultiValueJob(UpdateMultiValueJob model)
        {
            GenericResponse<bool> response;
            try
            {
                var filter2 = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, model.WorkflowStepInstanceId);
                var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, model.ActionCode);
                var filter4 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
                foreach (var itemValueJob in model.ItemValueJobs)
                {
                    var filter1 = Builders<Job>.Filter.Eq(x => x.FilePartInstanceId, itemValueJob.FilePartInstanceId);

                    var updateValue = Builders<Job>.Update
                        .Set(s => s.Value, itemValueJob.Value)
                        .Set(s => s.RightStatus, (short)EnumJob.RightStatus.Correct)
                        .Set(s => s.LastModificationDate, DateTime.UtcNow)
                        .Set(s => s.Status, (short)EnumJob.Status.Complete);

                    await _repos.UpdateOneAsync(filter1 & filter2 & filter3 & filter4, updateValue);
                }

                response = GenericResponse<bool>.ResultWithData(true);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<long>> GetCountJobByUser(Guid userInstanceId, Guid wflsConfig)
        {
            GenericResponse<long> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId) & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wflsConfig);
                var data = await _repos.CountAsync(filter);
                response = GenericResponse<long>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<long>> DeleteMultiByDocAsync(Guid docInstanceId)
        {
            GenericResponse<long> response;
            var jobsForDelete = await _repository.GetJobsByDocInstanceId(docInstanceId);
            var result = await _repository.DeleteMultiByDocAsync(docInstanceId);

            //sync data to Job Distribution by publish LogJobEvent
            jobsForDelete.ForEach(x => x.Status = (short)EnumJob.Status.Ignore);

            var logJobEvt = new LogJobEvent
            {
                LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobsForDelete),
            };
            await PublishEvent<LogJobEvent>(logJobEvt);


            response = GenericResponse<long>.ResultWithData(result);
            return response;
        }


        #region Get List JobDto with different arguments

        public async Task<GenericResponse<List<JobDto>>> GetListJobByUserProject(Guid userInstanceId, Guid projectInstanceId)
        {
            GenericResponse<List<JobDto>> response;
            var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId) & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
            var data = await _repos.FindAsync(filter);
            var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
            response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            return response;
        }

        public async Task<GenericResponse<long>> GetCountJobByUserProject(Guid userInstanceId, Guid projectInstanceId, Guid? workflowStepInstanceId = null)
        {
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId) & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                if (workflowStepInstanceId.HasValue)
                {
                    filter = filter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId.Value);
                }
                var data = await _repository.CountAsync(filter);

                return GenericResponse<long>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByProjectInstanceId(Guid projectInstanceId)
        {
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId) & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.Ocr));
                var data = await _repository.FindAsync(filter);
                var lstJobs = _mapper.Map<List<Job>, List<JobDto>>(data);

                return GenericResponse<List<JobDto>>.ResultWithData(lstJobs);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobCompleteByStatus(Guid projectInstanceId, int status)
        {
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId)
                & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.Ocr))
                & Builders<Job>.Filter.Eq(x => x.Status, status);
                var data = await _repository.FindAsync(filter);
                var lstJobs = _mapper.Map<List<Job>, List<JobDto>>(data);

                return GenericResponse<List<JobDto>>.ResultWithData(lstJobs);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

        }

        public async Task<GenericResponse<long>> GetCountJobByStatusActionCode(Guid projectInstanceId, int status = 0, string actionCode = null)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
            if (status != 0)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.Status, status);
            }
            if (actionCode != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            }
            var count = await _repository.CountAsync(filter);
            return GenericResponse<long>.ResultWithData(count);
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByProjectAndWorkflowStepInstanceId(Guid projectInstanceId, Guid workflowstepInstanceId)
        {
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowstepInstanceId) & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var jobs = await _repos.FindAsync(filter);
                var result = _mapper.Map<List<Job>, List<JobDto>>(jobs);
                return GenericResponse<List<JobDto>>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                return GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByFilterCode(string code, string strDocInstanceids, Guid projectInstanceId)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var regexCode = new BsonRegularExpression(code);
                var filter = Builders<Job>.Filter.Regex(x => x.Code, regexCode) & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                if (!string.IsNullOrEmpty(strDocInstanceids))
                {
                    var lstDocInstanceId = JsonConvert.DeserializeObject<List<Guid?>>(strDocInstanceids);
                    if (lstDocInstanceId != null && lstDocInstanceId.Count > 0)
                    {
                        filter = filter & Builders<Job>.Filter.In(x => x.DocInstanceId, lstDocInstanceId);
                    }
                }

                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobByStatusActionCode(Guid projectInstanceId, int status = 0, string actionCode = null)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                if (status != 0)
                {
                    filter = filter & Builders<Job>.Filter.Eq(x => x.Status, status);
                }
                if (actionCode != null)
                {
                    filter = filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }
                var data = await _repository.FindAsync(filter);
                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        #endregion

        #region History Job

        public async Task<GenericResponse<HistoryJobDto>> GetHistoryJobByUser(PagingRequest request, string actionCode, string accessToken)
        {
            GenericResponse<HistoryJobDto> response;
            try
            {
                #region filter & short
                string statusFilterValue = "";
                string codeFilterValue = "";
                string pathFilterValue = "";
                string actionCodeValue = "";
                string workFlowStepValue = "";
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //DocPath
                    var pathFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (pathFilter != null)
                    {
                        pathFilterValue = pathFilter.Value.Trim();
                    }
                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        statusFilterValue = statusFilter.Value.Trim();
                    }

                    //Code
                    var codeFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        codeFilterValue = codeFilter.Value.Trim();
                    }

                    //ActionCode
                    var actionCodeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ActionCode)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (actionCodeFilter != null)
                    {
                        actionCodeValue = actionCodeFilter.Value.Trim();
                    }
                    var workFlowStepFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.WorkflowStepInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (workFlowStepFilter != null)
                    {
                        workFlowStepValue = workFlowStepFilter.Value.Trim();
                    }
                }
                // Nếu không có Status truyền vào thì mặc định Status là Complete
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                if (!string.IsNullOrEmpty(codeFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                }
                //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                if (!string.IsNullOrEmpty(pathFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                }
                if (!string.IsNullOrEmpty(actionCode))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }
                if (!string.IsNullOrEmpty(actionCodeValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCodeValue);
                }
                if (!string.IsNullOrEmpty(workFlowStepValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, Guid.Parse(workFlowStepValue));
                }

                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocInstanceId
                    var docInstanceIdFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                    }


                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                        }
                        else
                        {
                            var reasonIgnoreFilter = Builders<Job>.Filter.Regex(x => x.ReasonIgnore, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                            var docNameRegexFilter = Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                            lastFilter = lastFilter & (reasonIgnoreFilter | docNameRegexFilter);
                        }
                    }

                    //JobCode
                    var codeFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //NormalState
                    var normalState = request.Filters.Where(_ => _.Field != null && _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (normalState != null)
                    {
                        var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                        if (canParse)
                        {
                            switch (actionCode)
                            {
                                case ActionCodeConstants.DataEntry:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                    }
                                    break;
                                case ActionCodeConstants.DataCheck:
                                case ActionCodeConstants.CheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                case ActionCodeConstants.QACheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }
                    else
                    {
                        if (projectInstanceIdFilter == null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                        }
                        else if (!string.IsNullOrEmpty(statusFilterValue) && short.Parse(statusFilterValue).Equals((short)EnumJob.Status.Ignore)) // Nếu trạng thái là bỏ qua
                        {
                            // do nothing
                        }
                        else
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                        }
                    }

                    //RightStatus =>//EnumJob.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    //IsIgnore
                    var isIgnoreFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isIgnoreFilter != null)
                    {
                        var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                        if (canParse/* && isIgnore*/)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                        }
                    }
                    //NumOfRound
                    var isNumOfRoundFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isNumOfRoundFilter != null)
                    {
                        var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                        if (canParse)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                        }
                    }
                    //QAStatus
                    var qAStautsFilter = request.Filters.Where(_ => _.Field != null && _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (qAStautsFilter != null)
                    {
                        var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                        if (canParse)
                        {
                            //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                            //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                        }
                    }
                    //MultiDocPath
                    var pathFilters = request.Filters.Where(_ => _.Field != null && _.Field.Equals("slTreeFolder") && !string.IsNullOrWhiteSpace(_.Value)).ToList();
                    if (!pathFilters.Any())
                    {
                        var haveChildFilters = request.Filters.Where(_ => _.Field == null && _.Filters.Any()).ToList();
                        foreach (var item in haveChildFilters)
                        {
                            pathFilters.AddRange(item.Filters.Where(x => x.Field != null && x.Field.Equals("slTreeFolder") && !string.IsNullOrWhiteSpace(x.Value)));
                        }
                    }
                    if (pathFilters.Count > 0)
                    {
                        var folderIdFilters = new List<FilterDefinition<Job>>();
                        foreach (var pathFlter in pathFilters)
                        {
                            folderIdFilters.Add(Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFlter.Value.ToString()}")));
                        }
                        if (folderIdFilters.Any())
                        {
                            lastFilter &= Builders<Job>.Filter.Or(folderIdFilters);
                        }
                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }
                #endregion
                //refator: Nếu chỉ view thông tin job thì không cần paging -> 
                // -> do hàm paging phải count nên rất chậm
                var lst = new PagedListExtension<Job>();
                if (request.PageInfo != null)
                {
                    lst = await _repository.GetPagingExtensionAsync(lastFilter, baseOrder, request.PageInfo.PageIndex, request.PageInfo.PageSize);
                }
                else
                {
                    var findJobData = await _repository.FindAsync(lastFilter);
                    lst.Data = findJobData;
                }

                var data = _mapper.Map<List<JobDto>>(lst.Data);

                //xử lý bổ sung thêm các dữ liệu trước khi return => không cần xử lý nếu là export 
                if (data != null && data.Any() && request.PageInfo?.PageIndex != -1)
                {
                    //Lấy danh sách complate cho các Job
                    var listComplain = new List<Complain>();
                    var batchSize = 1000;
                    var totalBatchPage = (int)Math.Ceiling(data.Count() / (double)batchSize);

                    for (var batchPage = 0; batchPage < totalBatchPage; batchPage++)
                    {
                        var batchItem = data.Skip(batchPage * batchSize).Take(batchSize);
                        if (batchItem.Any())
                        {
                            var listJobCode = batchItem.Select(x => x.Code).ToList();
                            var batchListComplain = await _complainRepository.GetByMultipleJobCode(listJobCode);
                            listComplain.AddRange(batchListComplain);
                        }
                    }

                    //Lấy thông tin ngày hoàn thành Job
                    var syntheticDataJobs = new List<Job>();
                    var docInstanceIds = data.Where(x => x.DocInstanceId != null && x.DocInstanceId != Guid.Empty)
                        .Select(x => x.DocInstanceId).Distinct();

                    totalBatchPage = (int)Math.Ceiling(docInstanceIds.Count() / (double)batchSize);

                    for (var batchPage = 0; batchPage < totalBatchPage; batchPage++)
                    {
                        var batchDocInstanceId = docInstanceIds.Skip(batchPage * batchSize).Take(batchSize);
                        if (batchDocInstanceId.Any())
                        {
                            var batchSyntheticJob = await _repository.GetJobByDocs(batchDocInstanceId,
                                                            ActionCodeConstants.SyntheticData, (short)EnumJob.Status.Complete);

                            syntheticDataJobs.AddRange(batchSyntheticJob);
                        }
                    }


                    Parallel.ForEach(data, item =>
                    {
                        var syntheticDataJob = syntheticDataJobs?.FirstOrDefault(x => x.DocInstanceId == item.DocInstanceId);
                        if (syntheticDataJob != null)
                        {
                            item.DocCompleteDate = syntheticDataJob.LastModificationDate;
                        }

                        var complain = listComplain.FirstOrDefault(x => x.JobCode.Equals(item.Code));
                        if (complain != null)
                        {
                            item.LastComplain = _mapper.Map<ComplainDto>(complain);
                        }
                    });
                }

                var pagedList = new PagedListExtension<JobDto>
                {
                    PageIndex = lst.PageIndex,
                    PageSize = lst.PageSize,
                    TotalCount = lst.TotalCount,
                    TotalCorrect = lst.TotalCorrect,
                    TotalComplete = lst.TotalComplete,
                    TotalWrong = lst.TotalWrong,
                    TotalIsIgnore = lst.TotalIsIgnore,
                    TotalFilter = lst.TotalFilter,
                    TotalPages = lst.TotalPages,
                    Data = data
                };
                //CountAbnormalJob 

                var countAbnormalJob = 0;
                switch (actionCode)
                {
                    case ActionCodeConstants.DataEntry:
                        var filterCount = lastFilter & Builders<Job>.Filter.Eq(_ => _.IsIgnore, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount)));
                        break;
                    case ActionCodeConstants.DataCheck:
                    case ActionCodeConstants.CheckFinal:
                        var filterCount1 = lastFilter & Builders<Job>.Filter.Eq(_ => _.HasChange, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount1)));
                        break;
                    case ActionCodeConstants.QACheckFinal:
                        var filterCount2 = lastFilter & Builders<Job>.Filter.Eq(_ => _.HasChange, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount2)));
                        break;
                    default:
                        break;
                }



                var result = new HistoryJobDto(pagedList, countAbnormalJob);
                response = GenericResponse<HistoryJobDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                response = GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }
        public async Task<GenericResponse<HistoryJobDto>> GetHistoryJobByUserV2(PagingRequest request, string wfsInstanceId, string accessToken)
        {
            GenericResponse<HistoryJobDto> response;
            try
            {
                #region filter & short
                string statusFilterValue = "";
                string codeFilterValue = "";
                string pathFilterValue = "";
                string actionCodeValue = "";
                string workFlowStepValue = "";
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //DocPath
                    var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (pathFilter != null)
                    {
                        pathFilterValue = pathFilter.Value.Trim();
                    }
                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        statusFilterValue = statusFilter.Value.Trim();
                    }

                    //Code
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        codeFilterValue = codeFilter.Value.Trim();
                    }

                    //ActionCode
                    var actionCodeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ActionCode)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (actionCodeFilter != null)
                    {
                        actionCodeValue = actionCodeFilter.Value.Trim();
                    }
                    var workFlowStepFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.WorkflowStepInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (workFlowStepFilter != null)
                    {
                        workFlowStepValue = workFlowStepFilter.Value.Trim();
                    }
                }
                // Nếu không có Status truyền vào thì mặc định Status là Complete
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                if (!string.IsNullOrEmpty(codeFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                }
                //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                if (!string.IsNullOrEmpty(pathFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                }

                //ProjectInstanceId
                var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (projectInstanceIdFilter != null)
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                }

                var actionCode = string.Empty;
                if (!string.IsNullOrEmpty(wfsInstanceId))
                {
                    var wfs = await _workflowStepClientService.GetByWorkflowInstanceIdAsync(Guid.Parse(wfsInstanceId), accessToken);
                    if (wfs != null && wfs.Data.Count > 0)
                    {
                        actionCode = wfs.Data.FirstOrDefault(x => x.InstanceId == Guid.Parse(wfsInstanceId))?.InstanceId.ToString();
                    }
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, Guid.Parse(wfsInstanceId));
                }
                if (!string.IsNullOrEmpty(actionCodeValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCodeValue);
                }
                if (!string.IsNullOrEmpty(workFlowStepValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, Guid.Parse(workFlowStepValue));
                }

                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocInstanceId
                    var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                    }

                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                        }
                        else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                    }

                    //JobCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //NormalState
                    var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (normalState != null)
                    {
                        var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                        if (canParse)
                        {
                            switch (actionCode)
                            {
                                case ActionCodeConstants.DataEntry:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                    }
                                    break;
                                case ActionCodeConstants.DataCheck:
                                case ActionCodeConstants.CheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                case ActionCodeConstants.QACheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }
                    else
                    {
                        if (projectInstanceIdFilter == null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                        }
                        else
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                        }
                    }

                    //RightStatus =>//EnumJob.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    //IsIgnore
                    var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isIgnoreFilter != null)
                    {
                        var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                        if (canParse/* && isIgnore*/)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                        }
                    }
                    //NumOfRound
                    var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isNumOfRoundFilter != null)
                    {
                        var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                        if (canParse)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                        }
                    }
                    //QAStatus
                    var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (qAStautsFilter != null)
                    {
                        var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                        if (canParse)
                        {
                            //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                            //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                        }
                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }
                #endregion
                //refator: Nếu chỉ view thông tin job thì không cần paging -> 
                // -> do hàm paging phải count nên rất chậm
                var lst = new PagedListExtension<Job>();
                if (request.PageInfo != null)
                {
                    lst = await _repository.GetPagingExtensionAsync(lastFilter, baseOrder, request.PageInfo.PageIndex, request.PageInfo.PageSize);
                }
                else
                {
                    var findJobData = await _repository.FindAsync(lastFilter);
                    lst.Data = findJobData;
                }

                var data = _mapper.Map<List<JobDto>>(lst.Data);

                //xử lý bổ sung thêm các dữ liệu trước khi return => không cần xử lý nếu là export 
                if (data != null && data.Any() && request.PageInfo?.PageIndex != -1)
                {
                    //Lấy danh sách complate cho các Job
                    var listComplain = new List<Complain>();
                    var batchSize = 1000;
                    var totalBatchPage = (int)Math.Ceiling(data.Count() / (double)batchSize);

                    for (var batchPage = 0; batchPage < totalBatchPage; batchPage++)
                    {
                        var batchItem = data.Skip(batchPage * batchSize).Take(batchSize);
                        if (batchItem.Any())
                        {
                            var listJobCode = batchItem.Select(x => x.Code).ToList();
                            var batchListComplain = await _complainRepository.GetByMultipleJobCode(listJobCode);
                            listComplain.AddRange(batchListComplain);
                        }
                    }

                    //Lấy thông tin ngày hoàn thành Job
                    var syntheticDataJobs = new List<Job>();
                    var docInstanceIds = data.Where(x => x.DocInstanceId != null && x.DocInstanceId != Guid.Empty)
                        .Select(x => x.DocInstanceId).Distinct();

                    totalBatchPage = (int)Math.Ceiling(docInstanceIds.Count() / (double)batchSize);

                    for (var batchPage = 0; batchPage < totalBatchPage; batchPage++)
                    {
                        var batchDocInstanceId = docInstanceIds.Skip(batchPage * batchSize).Take(batchSize);
                        if (batchDocInstanceId.Any())
                        {
                            var batchSyntheticJob = await _repository.GetJobByDocs(batchDocInstanceId,
                                                            ActionCodeConstants.SyntheticData, (short)EnumJob.Status.Complete);

                            syntheticDataJobs.AddRange(batchSyntheticJob);
                        }
                    }


                    Parallel.ForEach(data, item =>
                    {
                        var syntheticDataJob = syntheticDataJobs?.FirstOrDefault(x => x.DocInstanceId == item.DocInstanceId);
                        if (syntheticDataJob != null)
                        {
                            item.DocCompleteDate = syntheticDataJob.LastModificationDate;
                        }

                        var complain = listComplain.FirstOrDefault(x => x.JobCode.Equals(item.Code));
                        if (complain != null)
                        {
                            item.LastComplain = _mapper.Map<ComplainDto>(complain);
                        }
                    });
                }

                var pagedList = new PagedListExtension<JobDto>
                {
                    PageIndex = lst.PageIndex,
                    PageSize = lst.PageSize,
                    TotalCount = lst.TotalCount,
                    TotalCorrect = lst.TotalCorrect,
                    TotalComplete = lst.TotalComplete,
                    TotalWrong = lst.TotalWrong,
                    TotalIsIgnore = lst.TotalIsIgnore,
                    TotalFilter = lst.TotalFilter,
                    TotalPages = lst.TotalPages,
                    Data = data
                };
                //CountAbnormalJob 

                var countAbnormalJob = 0;
                switch (actionCode)
                {
                    case ActionCodeConstants.DataEntry:
                        var filterCount = lastFilter & Builders<Job>.Filter.Eq(_ => _.IsIgnore, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount)));
                        break;
                    case ActionCodeConstants.DataCheck:
                    case ActionCodeConstants.CheckFinal:
                        var filterCount1 = lastFilter & Builders<Job>.Filter.Eq(_ => _.HasChange, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount1)));
                        break;
                    case ActionCodeConstants.QACheckFinal:
                        var filterCount2 = lastFilter & Builders<Job>.Filter.Eq(_ => _.HasChange, true);
                        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount2)));
                        break;
                    default:
                        break;
                }



                var result = new HistoryJobDto(pagedList, countAbnormalJob);
                response = GenericResponse<HistoryJobDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                response = GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<long>> GetCountHistoryJobByUserForExportAsync(PagingRequest request, string actionCode, string accessToken)
        {
            #region filter & short
            string projectInstanceId = "";
            string statusFilterValue = "";
            string codeFilterValue = "";
            string pathFilterValue = "";
            if (request.Filters != null && request.Filters.Count > 0)
            {
                //DocPath
                var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (pathFilter != null)
                {
                    pathFilterValue = pathFilter.Value.Trim();
                }
                //Status
                var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (statusFilter != null)
                {
                    statusFilterValue = statusFilter.Value.Trim();
                }

                //Code
                var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (codeFilter != null)
                {
                    codeFilterValue = codeFilter.Value.Trim();
                }
            }
            // Nếu không có Status truyền vào thì mặc định Status là Complete
            var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
            if (!string.IsNullOrEmpty(codeFilterValue))
            {
                baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
            }
            //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
            if (!string.IsNullOrEmpty(pathFilterValue))
            {
                baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
            }
            if (!string.IsNullOrEmpty(actionCode))
            {
                baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            }

            var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

            //if (_userPrincipalService == null)
            //{
            //    return null;
            //}

            var lastFilter = baseFilter;

            //Apply thêm filter
            if (request.Filters != null && request.Filters.Count > 0)
            {
                //StartDate
                var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (startDateFilter != null)
                {
                    var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                    if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                }

                //endDate
                var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (endDateFilter != null)
                {
                    var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                    if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                }

                //DocInstanceId
                var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (docInstanceIdFilter != null)
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                }


                //DocName
                var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (docNameFilter != null)
                {
                    if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                    }
                    else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                }

                //JobCode
                var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (codeFilter != null)
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                }

                //NormalState
                var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (normalState != null)
                {
                    var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                    if (canParse)
                    {
                        switch (actionCode)
                        {
                            case ActionCodeConstants.DataEntry:
                                if (stateValue == 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                }
                                if (stateValue > 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                }
                                break;
                            case ActionCodeConstants.DataCheck:
                            case ActionCodeConstants.CheckFinal:
                                if (stateValue == 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                }
                                if (stateValue > 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                }
                                break;
                            case ActionCodeConstants.QACheckFinal:
                                if (stateValue == 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                }
                                if (stateValue > 0)
                                {
                                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }

                //ProjectInstanceId
                var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (projectInstanceIdFilter != null)
                {
                    projectInstanceId = projectInstanceIdFilter.Value;
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                }

                //UserInstanceId
                var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (userInstanceIdFilter != null)
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                }
                else
                {
                    if (projectInstanceIdFilter == null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                    }
                    else
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                    }
                }

                //RightStatus =>//EnumJob.RightStatus
                var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (statusFilter != null)
                {
                    var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                    if (canParse && statusValue >= 0)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                    }

                }
                //IsIgnore
                var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (isIgnoreFilter != null)
                {
                    var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                    if (canParse/* && isIgnore*/)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                    }
                }
                //NumOfRound
                var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (isNumOfRoundFilter != null)
                {
                    var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                    if (canParse)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                    }
                }
                //QAStatus
                var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                if (qAStautsFilter != null)
                {
                    var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                    if (canParse)
                    {
                        //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                        //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                    }
                }
            }
            else
            {
                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
            }
            //Apply thêm sort
            if (request.Sorts != null && request.Sorts.Count > 0)
            {
                var isValidSort = false;
                SortDefinition<Job> newSort = null;
                foreach (var item in request.Sorts)
                {
                    if (typeof(Job).GetProperty(item.Field) != null)
                    {
                        if (!isValidSort)
                        {
                            newSort = item.OrderDirection == OrderDirection.Asc ?
                               Builders<Job>.Sort.Ascending(item.Field)
                               : Builders<Job>.Sort.Descending(item.Field);
                        }
                        else
                        {
                            newSort = item.OrderDirection == OrderDirection.Asc ?
                               newSort.Ascending(item.Field)
                               : newSort.Descending(item.Field);
                        }


                        isValidSort = true;
                    }
                }

                if (isValidSort) baseOrder = newSort;
            }
            #endregion
            
            var result = await _repository.CountAsync(lastFilter);
            return GenericResponse<long>.ResultWithData(result);
        }

        /// <summary>
        /// Hàm xuất dữ liệu ra file excel. 
        /// Nếu có nhiều hơn 100k bản ghi thì giới hạn mỗi file excel 100k bản ghi
        /// Trả về file nén thư mục chứa các file excel
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<byte[]> ExportExcelHistoryJobByUserV2(PagingRequest request, string actionCode, string accessToken)
        {
            byte[] result;
            int batchSize = 100000;
            int currentBatchSize = 0;
            int fileNumber = 1;
            string tempDirectory = Path.Combine(Path.GetTempPath(), "ExportExcelHistoryJob");
            if (!Directory.Exists(tempDirectory))
            {
                Directory.CreateDirectory(tempDirectory);
            }
            string currentFilePath = Path.Combine(tempDirectory, $"file_{fileNumber}.xlsx");
            try
            {
                var allData = new List<Dictionary<string, object>>();
                string projectInstanceId = "";
                #region filter & short
                string statusFilterValue = "";
                string codeFilterValue = "";
                string pathFilterValue = "";
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //DocPath
                    var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (pathFilter != null)
                    {
                        pathFilterValue = pathFilter.Value.Trim();
                    }
                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        statusFilterValue = statusFilter.Value.Trim();
                    }

                    //Code
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        codeFilterValue = codeFilter.Value.Trim();
                    }
                }
                // Nếu không có Status truyền vào thì mặc định Status là Complete
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                if (!string.IsNullOrEmpty(codeFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                }
                //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                if (!string.IsNullOrEmpty(pathFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                }
                if (!string.IsNullOrEmpty(actionCode))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }

                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return null;
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocInstanceId
                    var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                    }


                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                        }
                        else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                    }

                    //JobCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //NormalState
                    var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (normalState != null)
                    {
                        var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                        if (canParse)
                        {
                            switch (actionCode)
                            {
                                case ActionCodeConstants.DataEntry:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                    }
                                    break;
                                case ActionCodeConstants.DataCheck:
                                case ActionCodeConstants.CheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                case ActionCodeConstants.QACheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        projectInstanceId = projectInstanceIdFilter.Value;
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }
                    else
                    {
                        if (projectInstanceIdFilter == null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                        }
                        else
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                        }
                    }

                    //RightStatus =>//EnumJob.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    //IsIgnore
                    var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isIgnoreFilter != null)
                    {
                        var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                        if (canParse/* && isIgnore*/)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                        }
                    }
                    //NumOfRound
                    var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isNumOfRoundFilter != null)
                    {
                        var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                        if (canParse)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                        }
                    }
                    //QAStatus
                    var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (qAStautsFilter != null)
                    {
                        var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                        if (canParse)
                        {
                            //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                            //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                        }
                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }
                #endregion

                IEnumerable<SelectListItem> lstActionCode = new List<SelectListItem>();
                var project = await _projectClientService.GetByInstanceIdAsync(Guid.Parse(projectInstanceId), accessToken);
                if (project.Data.WorkflowInstanceId.HasValue && project.Data.WorkflowInstanceId != Guid.Empty)
                {
                    var workflowDto = await _workflowClientService.GetByInstanceIdAsync(project.Data.WorkflowInstanceId.Value, accessToken);
                    var wfDto = workflowDto.Data ?? new WorkflowDto();

                    lstActionCode = wfDto?.LstWorkflowStepDto?.Select(x => new SelectListItem { Text = x.Name, Value = x.ActionCode });
                    if (workflowDto != null)
                    {
                        var wfData = await _workflowClientService.GetByInstanceIdAsync(workflowDto.Data.InstanceId, accessToken);
                    }
                }
                var lstUser = new List<UserDto>();
                var lstUserInstanceId = new List<Guid>();
                var lstUserJob = await GetListUserInstanceIdByProject(Guid.Parse(projectInstanceId));
                if (lstUserJob.Success && lstUserJob.Data.Any())
                {
                    lstUserInstanceId = lstUserJob.Data;
                }
                if (lstUserInstanceId.Any())
                {
                    var lstUserResponse = await _appUserClientService.GetUserInfoes(JsonConvert.SerializeObject(lstUserInstanceId), accessToken);
                    if (lstUserResponse.Success && lstUserResponse.Data.Any())
                    {
                        lstUser = lstUserResponse.Data.ToList();
                    }
                }

                //long totalRow = 0;

                var findOptions = new FindOptions<Job> { BatchSize = 1000 };
                var cursor = await _repository.GetCursorListJobAsync(lastFilter, findOptions);
                var data = new List<Dictionary<string, object>>();
                while (await cursor.MoveNextAsync())
                {
                    var currentBatch = cursor.Current;
                    var lstDocPathName = new Dictionary<string, string>();
                    var lstDocPath = currentBatch.Select(x => x.DocPath).ToList().Distinct();
                    var requestLstDocPath = JsonConvert.SerializeObject(lstDocPath);
                    var resultDocPathName = await _docClientService.GetMultiPathNameByMultiDocPath(requestLstDocPath, accessToken);
                    if (resultDocPathName != null)
                    {
                        lstDocPathName = resultDocPathName.Data;
                    }
                    foreach (var job in currentBatch)
                    {
                        try
                        {
                            #region Status,QAStatus
                            var status = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.DataEntry))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.WaitingConfirm)
                                {
                                    status = "Chờ xác nhận";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Correct)
                                {
                                    status = "Đã xử lý đúng";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Wrong)
                                {
                                    status = "Đã xử lý sai";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else if (job.ActionCode == nameof(ActionCodeConstants.DataCheck))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.Status == (int)EnumJob.Status.Complete)

                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else
                            {
                                if (job.Status == (int)EnumJob.Status.Complete)
                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            var statusQA = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.QACheckFinal))
                            {
                                if (job.QaStatus == true)
                                {
                                    statusQA = "Pass";
                                }
                                else if (job.QaStatus == false)
                                {
                                    statusQA = "Fail";
                                }
                            }
                            #endregion
                            #region pathName
                            var pathName = lstDocPathName != null ? lstDocPathName.GetValueOrDefault(job.DocPath) + "/" + job.DocName ?? "" : "";
                            #endregion
                            var wfStepName = lstActionCode.Where(x => x.Value == job.ActionCode).FirstOrDefault()?.Text;
                            string price = "0";
                            if (job.RightStatus == (int)EnumJob.RightStatus.Wrong || job.IsIgnore)
                            {
                                price = "0";
                            }
                            else
                            {
                                price = String.Format(CultureInfo.InvariantCulture, "{0:N}", job.Price);
                            }
                            var userFullName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.FullName;
                            var userName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.UserName;
                            var start = job.ReceivedDate.HasValue ? job.ReceivedDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var end = job.DueDate.HasValue ? job.DueDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var value = string.Empty;
                            if (job.ActionCode == ActionCodeConstants.SegmentLabeling || job.ActionCode == ActionCodeConstants.DataEntry || job.ActionCode == ActionCodeConstants.CheckFinal || job.ActionCode == ActionCodeConstants.QACheckFinal)
                            {
                                value = job.DocName;
                            }
                            else value = job.Value;
                            var row = new Dictionary<string, object>
                            {
                                ["Mã công việc"] = GetSafeValue(job.Code),
                                ["Đường dẫn"] = GetSafeValue(pathName),
                                ["Nội dung"] = GetSafeValue(value),
                                ["Loại công việc"] = GetSafeValue(wfStepName),
                                ["Nhân sự"] = GetSafeValue(userFullName + " - " + userName),
                                ["Thời gian nhận dữ liệu"] = GetSafeValue(start),
                                ["Thời gian hoàn thành dữ liệu"] = GetSafeValue(end),
                                ["Trạng thái"] = GetSafeValue(status),
                                ["Kết quả QA"] = GetSafeValue(statusQA),
                                ["Số lần bị QA trả lại"] = job.NumOfRound.ToString(),
                                ["Điểm thanh toán"] = GetSafeValue(price),
                                ["Lý do trả lại"] = GetSafeValue(job.Note),
                            };
                            data.Add(row);
                            currentBatchSize++;
                            if (currentBatchSize >= batchSize && data.Count() >= batchSize)
                            {
                                using (var fileStream = new FileStream(currentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                                {
                                    await MiniExcel.SaveAsAsync(fileStream, data);
                                    fileStream.Dispose();
                                }
                                currentBatchSize = 0;
                                fileNumber++;
                                currentFilePath = Path.Combine(tempDirectory, $"file_{fileNumber}.xlsx");
                                data.Clear();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex.Message);
                            throw new Exception(ex.Message);
                        }
                    }
                    if (lstDocPathName != null)
                    {
                        lstDocPathName.Clear();
                    }
                    //data.Clear();

                }
                if (currentBatchSize < batchSize && data.Count() < batchSize) // Trường hợp dữ liệu không tới 100k bản ghi
                {
                    using (var fileStream = new FileStream(currentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        await MiniExcel.SaveAsAsync(fileStream, data);
                        fileStream.Dispose();
                    }
                }
                string zipFilePath = Path.Combine(Path.GetTempPath(), "JobHistoryFiles.zip");
                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }
                ZipFile.CreateFromDirectory(tempDirectory, zipFilePath);
                result = await File.ReadAllBytesAsync(zipFilePath);
            }
            catch (Exception ex)
            {
                Log.Debug(ex.Message);
                throw new Exception(ex.Message);
            }
            finally
            {
                //Xóa thư mục temp
                Directory.Delete(tempDirectory, true);
            }
            return result;
        }
        /// <summary>
        /// Hàm xuất dữ liệu ra file excel. 
        /// Có bao nhiêu dữ liệu xuất hết vào một file excel
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<byte[]> ExportExcelHistoryJobByUser(PagingRequest request, string actionCode, string accessToken)
        {
            byte[] result;
            var tempFilePath = Path.GetTempFileName();
            try
            {
                var allData = new List<Dictionary<string, object>>();
                string projectInstanceId = "";
                #region filter & short
                string statusFilterValue = "";
                string codeFilterValue = "";
                string pathFilterValue = "";
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //DocPath
                    var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (pathFilter != null)
                    {
                        pathFilterValue = pathFilter.Value.Trim();
                    }
                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        statusFilterValue = statusFilter.Value.Trim();
                    }

                    //Code
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        codeFilterValue = codeFilter.Value.Trim();
                    }
                }
                // Nếu không có Status truyền vào thì mặc định Status là Complete
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                if (!string.IsNullOrEmpty(codeFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                }
                //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                if (!string.IsNullOrEmpty(pathFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                }
                if (!string.IsNullOrEmpty(actionCode))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }

                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return null;
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocInstanceId
                    var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                    }


                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                        }
                        else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                    }

                    //JobCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //NormalState
                    var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (normalState != null)
                    {
                        var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                        if (canParse)
                        {
                            switch (actionCode)
                            {
                                case ActionCodeConstants.DataEntry:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                    }
                                    break;
                                case ActionCodeConstants.DataCheck:
                                case ActionCodeConstants.CheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                case ActionCodeConstants.QACheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        projectInstanceId = projectInstanceIdFilter.Value;
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }
                    else
                    {
                        if (projectInstanceIdFilter == null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                        }
                        else
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                        }
                    }

                    //RightStatus =>//EnumJob.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    //IsIgnore
                    var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isIgnoreFilter != null)
                    {
                        var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                        if (canParse/* && isIgnore*/)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                        }
                    }
                    //NumOfRound
                    var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isNumOfRoundFilter != null)
                    {
                        var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                        if (canParse)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                        }
                    }
                    //QAStatus
                    var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (qAStautsFilter != null)
                    {
                        var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                        if (canParse)
                        {
                            //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                            //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                        }
                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }
                #endregion

                IEnumerable<SelectListItem> lstActionCode = new List<SelectListItem>();
                var project = await _projectClientService.GetByInstanceIdAsync(Guid.Parse(projectInstanceId), accessToken);
                if (project.Data.WorkflowInstanceId.HasValue && project.Data.WorkflowInstanceId != Guid.Empty)
                {
                    var workflowDto = await _workflowClientService.GetByInstanceIdAsync(project.Data.WorkflowInstanceId.Value, accessToken);
                    var wfDto = workflowDto.Data ?? new WorkflowDto();

                    lstActionCode = wfDto?.LstWorkflowStepDto?.Select(x => new SelectListItem { Text = x.Name, Value = x.ActionCode });
                    if (workflowDto != null)
                    {
                        var wfData = await _workflowClientService.GetByInstanceIdAsync(workflowDto.Data.InstanceId, accessToken);
                    }
                }
                var lstUser = new List<UserDto>();
                var lstUserInstanceId = new List<Guid>();
                var lstUserJob = await GetListUserInstanceIdByProject(Guid.Parse(projectInstanceId));
                if (lstUserJob.Success && lstUserJob.Data.Any())
                {
                    lstUserInstanceId = lstUserJob.Data;
                }
                if (lstUserInstanceId.Any())
                {
                    var lstUserResponse = await _appUserClientService.GetUserInfoes(JsonConvert.SerializeObject(lstUserInstanceId), accessToken);
                    if (lstUserResponse.Success && lstUserResponse.Data.Any())
                    {
                        lstUser = lstUserResponse.Data.ToList();
                    }
                }

                long totalRow = 0;

                var findOptions = new FindOptions<Job> { BatchSize = 1000 };
                var cursor = await _repository.GetCursorListJobAsync(lastFilter, findOptions);
                using (var fileStream = new FileStream(tempFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    var data = new List<Dictionary<string, object>>();
                    while (await cursor.MoveNextAsync())
                    {
                        var currentBatch = cursor.Current;

                        totalRow += currentBatch.Count();
                        var lstDocPathName = new Dictionary<string, string>();
                        var lstDocPath = currentBatch.Select(x => x.DocPath).ToList().Distinct();
                        var requestLstDocPath = JsonConvert.SerializeObject(lstDocPath);
                        var resultDocPathName = await _docClientService.GetMultiPathNameByMultiDocPath(requestLstDocPath, accessToken);
                        if (resultDocPathName != null)
                        {
                            lstDocPathName = resultDocPathName.Data;
                        }
                        foreach (var job in currentBatch)
                        {
                            #region Status,QAStatus
                            var status = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.DataEntry))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.WaitingConfirm)
                                {
                                    status = "Chờ xác nhận";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Correct)
                                {
                                    status = "Đã xử lý đúng";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Wrong)
                                {
                                    status = "Đã xử lý sai";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else if (job.ActionCode == nameof(ActionCodeConstants.DataCheck))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.Status == (int)EnumJob.Status.Complete)

                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else
                            {
                                if (job.Status == (int)EnumJob.Status.Complete)
                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            var statusQA = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.QACheckFinal))
                            {
                                if (job.QaStatus == true)
                                {
                                    statusQA = "Pass";
                                }
                                else if (job.QaStatus == false)
                                {
                                    statusQA = "Fail";
                                }
                            }
                            #endregion
                            #region pathName
                            var pathName = lstDocPathName.GetValueOrDefault(job.DocPath) + "/" + job.DocName ?? "";
                            #endregion
                            var wfStepName = lstActionCode.Where(x => x.Value == job.ActionCode).FirstOrDefault()?.Text;
                            string price = "0";
                            if (job.RightStatus == (int)EnumJob.RightStatus.Wrong || job.IsIgnore)
                            {
                                price = "0";
                            }
                            else
                            {
                                price = String.Format(CultureInfo.InvariantCulture, "{0:N}", job.Price);
                            }
                            var userFullName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.FullName;
                            var userName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.UserName;
                            var start = job.ReceivedDate.HasValue ? job.ReceivedDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var end = job.DueDate.HasValue ? job.DueDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var value = string.Empty;
                            if (job.ActionCode == ActionCodeConstants.SegmentLabeling || job.ActionCode == ActionCodeConstants.DataEntry || job.ActionCode == ActionCodeConstants.CheckFinal || job.ActionCode == ActionCodeConstants.QACheckFinal)
                            {
                                value = job.DocName;
                            }
                            else value = job.Value;
                            var row = new Dictionary<string, object>
                            {
                                ["Mã công việc"] = GetSafeValue(job.Code),
                                ["Đường dẫn"] = GetSafeValue(pathName),
                                ["Nội dung"] = GetSafeValue(value),
                                ["Loại công việc"] = GetSafeValue(wfStepName),
                                ["Nhân sự"] = GetSafeValue(userFullName + " - " + userName),
                                ["Thời gian nhận dữ liệu"] = GetSafeValue(start),
                                ["Thời gian hoàn thành dữ liệu"] = GetSafeValue(end),
                                ["Trạng thái"] = GetSafeValue(status),
                                ["Kết quả QA"] = GetSafeValue(statusQA),
                                ["Số lần bị QA trả lại"] = job.NumOfRound.ToString(),
                                ["Điểm thanh toán"] = GetSafeValue(price),
                                ["Lý do trả lại"] = GetSafeValue(job.Note),
                            };
                            allData.Add(row);
                            //Log.Debug("Row:" + JsonConvert.SerializeObject(row));
                        }

                        //SaveAsFile(fileStream, data);
                        //fileStream.SaveAs(data);
                        Log.Debug("ExportExcelTotalRow:" + totalRow);
                        lstDocPathName.Clear();
                        data.Clear();
                        //GC.Collect();

                    }
                    Log.Debug("ExportExcelTotalRowAll:" + totalRow);
                    SaveAsFile(fileStream, allData);
                    allData.Clear();
                    allData = null;
                    GC.Collect(); //Thu hồi lại memory
                }
                result = await File.ReadAllBytesAsync(tempFilePath);
            }
            finally
            {
                // Xóa file tạm thời
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            return result;
        }

        void SaveAsFile(FileStream fileStream, List<Dictionary<string, object>> data)
        {
            try
            {
                fileStream.SaveAs(data);
                fileStream.Flush();

                Log.Debug($"Data saved to file. Current file size: {fileStream.Length}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error while saving data to file: {ex.Message}");
            }
        }
        private string GetSafeValue(object value)
        {
            return value != null ? value.ToString() : "";
        }

        public async Task<GenericResponse<int>> BackIgnoreJobToCheckFinalProcess(JobResult result, string accessToken = null)
        {
            // 2. Process
            GenericResponse<int> response;
            try
            {
                var parse = ObjectId.TryParse(result.JobId, out ObjectId id);
                if (!parse)
                {
                    return GenericResponse<int>.ResultWithData(-1, "Không parse được Id");
                }

                var resultJob = await _repos.GetByIdAsync(id);
                //Kiểm tra nếu công việc không còn dữ liệu tại bảng Doc thì không cho phép trả về cho người dùng
                if (resultJob != null)
                {
                    var docInstanceIds = new List<Guid>();
                    Guid docInstanceId = resultJob.DocInstanceId ?? new Guid();
                    docInstanceIds.Add(docInstanceId);
                    var doc = await _docClientService.GetByInstanceIdAsync(docInstanceId, accessToken);
                    if (doc.Data != null)
                    {
                        if (doc.Data.IsActive == false)
                        {
                            return response = GenericResponse<int>.ResultWithData(-1, "Công việc này không còn tài liệu");
                        }
                    }
                }

                var filter1 = Builders<Job>.Filter.Eq(x => x.Id, id); // lấy theo id
                var filter2 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Ignore); // Lấy các job đang bị bỏ qua
                var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)); // ActionCode

                var job = await _repos.FindFirstAsync(filter1 & filter2 & filter3);
                Job resultUpdateJob = null;

                if (job == null)
                {
                    response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                    return response;
                }
                Job cloneJob = job;

                var now = DateTime.UtcNow;
                job.LastModificationDate = now;
                job.UserInstanceId = null;
                job.IsIgnore = false;
                //job.ReasonIgnore = "";
                job.Status = (short)EnumJob.Status.Waiting;

                //var resultUpdateJob = await _repos.UpdateAsync(job);
                resultUpdateJob = await _repos.ReplaceOneAsync(filter1, job);

                if (resultUpdateJob != null)
                {
                    // Publish message sang DistributionJob
                    var logJob = _mapper.Map<Job, LogJobDto>(job);
                    var logJobEvt = new LogJobEvent
                    {
                        LogJobs = new List<LogJobDto> { logJob },
                        AccessToken = accessToken
                    };
                    // Outbox
                    await PublishEvent<LogJobEvent>(logJobEvt);
                    response = GenericResponse<int>.ResultWithData(1);
                }
                else
                {
                    Log.Error($"BackToCheckFinalProcess fail: job => {job.Code}");
                    await _repos.UpdateAsync(cloneJob); // nếu ReplaceOneAsync lỗi thì update lại job về trạng thái ban đầu
                    response = GenericResponse<int>.ResultWithData(0);
                }
            }
            catch (Exception ex)
            {

                response = GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on BackToCheckFinalProcess => param: {JsonConvert.SerializeObject(result)};mess: {ex.Message} ; trace:{ex.StackTrace}");
            }
            return response;
        }

        public async Task<GenericResponse<int>> BackMultiIgnoreJobToCheckFinalProcess(List<JobResult> lstJobResult, string accessToken = null)
        {
            //GenericResponse<int> response;
            int countSuccess = 0;
            int countFail = 0;
            bool isNoDataDoc = false;
            try
            {
                if (lstJobResult != null && lstJobResult.Count > 0)
                {
                    foreach (var jobResult in lstJobResult)
                    {
                        var parse = ObjectId.TryParse(jobResult.JobId, out ObjectId id);
                        if (!parse)
                        {
                            //return GenericResponse<int>.ResultWithData(-1, "Không parse được Id");
                            Log.Error($"BackMultiJobToCheckFinalProcess failParseId: jobId => {jobResult.JobId.ToString()}");
                            countFail++;
                            continue;
                        }
                        var resultJob = await _repos.GetByIdAsync(id);
                        //Kiểm tra nếu công việc không còn dữ liệu tại bảng Doc thì không cho phép trả về cho người dùng
                        if (resultJob != null)
                        {
                            var docInstanceIds = new List<Guid>();
                            Guid docInstanceId = resultJob.DocInstanceId ?? new Guid();
                            docInstanceIds.Add(docInstanceId);
                            var doc = await _docClientService.GetByInstanceIdAsync(docInstanceId, accessToken);
                            if (doc.Data != null)
                            {
                                if (doc.Data.IsActive == false)
                                {
                                    Log.Error($"BackMultiJobToCheckFinalProcess NoDataDoc: jobId => {jobResult.JobId.ToString()} DocName: {resultJob.DocName.ToString()}");
                                    countFail++;
                                    isNoDataDoc = true;
                                    continue;
                                }
                            }
                        }
                        var filter1 = Builders<Job>.Filter.Eq(x => x.Id, id); // lấy theo id
                        var filter2 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Ignore); // Lấy các job đang bị bỏ qua
                        var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.CheckFinal)); // ActionCode

                        var job = await _repos.FindFirstAsync(filter1 & filter2 & filter3);
                        Job resultUpdateJob = null;

                        if (job == null)
                        {
                            //response = GenericResponse<int>.ResultWithData(-1, "Không thấy dữ liệu");
                            //return response;
                            Log.Error($"BackMultiJobToCheckFinalProcess failFindFirstAsync: jobId => {jobResult.JobId.ToString()}");
                            countFail++;
                            continue;
                        }
                        Job cloneJob = job;

                        var now = DateTime.UtcNow;
                        job.LastModificationDate = now;
                        job.UserInstanceId = null;
                        job.IsIgnore = false;
                        //job.ReasonIgnore = "";
                        job.Status = (short)EnumJob.Status.Waiting;

                        //var resultUpdateJob = await _repos.UpdateAsync(job);
                        resultUpdateJob = await _repos.ReplaceOneAsync(filter1, job);

                        if (resultUpdateJob != null)
                        {
                            // Publish message sang DistributionJob
                            var logJob = _mapper.Map<Job, LogJobDto>(job);
                            var logJobEvt = new LogJobEvent
                            {
                                LogJobs = new List<LogJobDto> { logJob },
                                AccessToken = accessToken
                            };
                            // Outbox
                            await PublishEvent<LogJobEvent>(logJobEvt);
                            countSuccess++;
                        }
                        else
                        {
                            Log.Error($"BackToCheckFinalProcess failReplaceOneAsync: job => {job.Code}");
                            countFail++;
                            await _repos.UpdateAsync(cloneJob); // nếu ReplaceOneAsync lỗi thì update lại job về trạng thái ban đầu
                        }
                    }
                    //response = GenericResponse<int>.ResultWithData(1,$"Thành công {countSuccess} file, lỗi {countFail} file");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error on BackToCheckFinalProcess => param: {JsonConvert.SerializeObject(lstJobResult)};mess: {ex.Message} ; trace:{ex.StackTrace}");
                return GenericResponse<int>.ResultWithError(-1, ex.Message, ex.StackTrace);
            }
            if (isNoDataDoc)
            {
                return GenericResponse<int>.ResultWithData(1, $"Thành công {countSuccess} file, lỗi {countFail} file vì không tồn tại tài liệu");
            }
            else return GenericResponse<int>.ResultWithData(1, $"Thành công {countSuccess} file, lỗi {countFail} file");
        }

        public async Task<GenericResponse<double>> GetFalsePercent(string accessToken)
        {
            if (_userPrincipalService == null)
            {
                return GenericResponse<double>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
            }

            GenericResponse<double> response;
            var userInstanceId = _userPrincipalService.UserInstanceId.GetValueOrDefault();
            try
            {
                string cacheKey = $"$@${userInstanceId}$@$FalsePercent";
                var minutesExpired = 1 * 60 * 60; // 1 hours 

                var result = _cachingHelper.TryGetFromCache<double?>(cacheKey);
                if (result == null)
                {
                    result = await _repository.GetFalsePercentAsync(userInstanceId);

                    await _cachingHelper.TrySetCacheAsync<double>(cacheKey, result.GetValueOrDefault(), minutesExpired);
                }
                response = GenericResponse<double>.ResultWithData(result.GetValueOrDefault());
            }
            catch (Exception ex)
            {

                response = GenericResponse<double>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<HistoryJobDto>> GetHistoryJobByStep(PagingRequest request,
            string projectInstanceId, string sActionCodes)
        {
            GenericResponse<HistoryJobDto> response;
            try
            {
                List<string> actionCodes = new List<string>();

                string[] arr = sActionCodes.Split(":");
                foreach (var str in arr)
                {
                    if (!string.IsNullOrEmpty(str))
                    {
                        actionCodes.Add(str);
                    }
                }

                var baseFilter = Builders<Job>.Filter.In(x => x.ActionCode, actionCodes);
                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                    }

                    ////JobCode
                    //var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    //if (codeFilter != null)
                    //{
                    //    lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    //}

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("Status") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);
                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.Status, statusValue);
                        }
                    }

                    ////RightStatus =>//EnumJob.RightStatus
                    //var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    //if (statusFilter != null)
                    //{
                    //    var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                    //    if (canParse && statusValue >= 0)
                    //    {
                    //        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                    //    }
                    //}
                    ////IsIgnore
                    //var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    //if (isIgnoreFilter != null)
                    //{
                    //    var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                    //    if (canParse/* && isIgnore*/)
                    //    {
                    //        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                    //    }
                    //}
                }

                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }

                var lst = await _repository.GetPagingExtensionAsync(lastFilter, baseOrder, request.PageInfo.PageIndex, request.PageInfo.PageSize);
                var pagedList = new PagedListExtension<JobDto>
                {
                    PageIndex = lst.PageIndex,
                    PageSize = lst.PageSize,
                    TotalCount = lst.TotalCount,
                    TotalCorrect = lst.TotalCorrect,
                    TotalComplete = lst.TotalComplete,
                    TotalWrong = lst.TotalWrong,
                    TotalIsIgnore = lst.TotalIsIgnore,
                    TotalFilter = lst.TotalFilter,
                    TotalPages = lst.TotalPages,
                    TotalError = lst.TotalError,
                    Data = _mapper.Map<List<JobDto>>(lst.Data)
                };
                //CountAbnormalJob 

                var countAbnormalJob = 0;
                //switch (actionCode)
                //{
                //    case ActionCodeConstants.DataEntry:
                //        var filterCount = lastFilter & Builders<Job>.Filter.Eq(_ => _.IsIgnore, true);
                //        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount)));
                //        break;
                //    case ActionCodeConstants.DataCheck:
                //    case ActionCodeConstants.CheckFinal:
                //        var filterCount2 = lastFilter & Builders<Job>.Filter.Eq(_ => _.HasChange, true);
                //        countAbnormalJob = unchecked((int)(await _repos.CountAsync(filterCount2)));
                //        break;
                //    default:
                //        break;
                //}



                var result = new HistoryJobDto(pagedList, countAbnormalJob);
                response = GenericResponse<HistoryJobDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                response = GenericResponse<HistoryJobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<PagedList<HistoryUserJobDto>>> GetPagingHistoryUser(PagingRequest request, string accessToken)
        {
            GenericResponse<PagedList<HistoryUserJobDto>> response;
            try
            {
                if (request.PageInfo == null)
                {
                    request.PageInfo = new PageInfo
                    {
                        PageIndex = 1,
                        PageSize = 10
                    };
                }

                if (request.PageInfo.PageIndex <= 0)
                {
                    return GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Page index must be greater or than 1");
                }
                if (request.PageInfo.PageSize < 0)
                {
                    return GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Page size must be greater or than 0");
                }

                if (_userPrincipalService == null)
                {
                    return GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete)
                    & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);

                var lastFilter = baseFilter;
                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //actionCode
                    var actionCodeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ActionCode)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (actionCodeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCodeFilter.Value);
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }
                }
                var lstJobDto = await _repository.FindAsync(lastFilter);
                if (lstJobDto.Count() <= 0)
                {
                    return GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithData(new PagedList<HistoryUserJobDto>());
                }
                var lstUserInstanceId = lstJobDto.Select(x => x.UserInstanceId).Distinct();
                var lstUserInfor = await _appUserClientService.GetUserInfoes(JsonConvert.SerializeObject(lstUserInstanceId), accessToken);
                var historyUser = new List<HistoryUserJobDto>();
                foreach (var item in lstUserInstanceId)
                {
                    var userInfor = lstUserInfor.Data.Where(x => x.InstanceId == item.Value).FirstOrDefault();
                    historyUser.Add(new HistoryUserJobDto
                    {
                        Name = userInfor.FullName,
                        UserInstanceId = item.Value,
                        TotalJob = lstJobDto.Where(x => x.UserInstanceId == item.Value).Count()
                    });
                }
                //filter by name user
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var nameFilter = request.Filters.Where(_ => _.Field.Equals("FullName") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (nameFilter != null)
                    {
                        historyUser = historyUser.Where(x => x.Name.Contains(nameFilter.Value)).ToList();
                    }
                }
                //order by name
                historyUser = historyUser.OrderBy(x => x.Name).ToList();
                var result = new PagedList<HistoryUserJobDto>
                {
                    Data = historyUser.OrderBy(x => x.Name).Skip((request.PageInfo.PageIndex - 1) * request.PageInfo.PageSize).Take(request.PageInfo.PageSize).ToList(),
                    PageIndex = request.PageInfo.PageIndex,
                    PageSize = request.PageInfo.PageSize,
                    TotalCount = historyUser.Count(),
                    TotalFilter = historyUser.Count(),
                    TotalPages = (int)Math.Ceiling((decimal)historyUser.Count() / request.PageInfo.PageSize)
                };
                response = GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<PagedList<HistoryUserJobDto>>.ResultWithData(null, ex.Message);
            }
            return response;
        }

        #endregion

        public async Task<GenericResponse<List<Guid>>> GetListUserInstanceIdByProject(Guid projectInstanceId)
        {
            GenericResponse<List<Guid>> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId)
                    & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                var lstUerInstanceId = await _repository.GetDistinctUserInstanceId(filter);

                response = GenericResponse<List<Guid>>.ResultWithData(lstUerInstanceId);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<Guid>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        #region Retry JOB

        // TODO: Turning
        public async Task<GenericResponse<List<ErrorDocReportSummary>>> GetErrorDocReportSummary(Guid projectInstanceId, string folderIds, string accessToken = null)
        {
            // Filter by projectInstanceId
            var result = new List<ErrorDocReportSummary>();
            var baseFilter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Error);
            FilterDefinition<Job> filterFolders = null;
            FilterDefinition<Job> filterFolder = null;
            // Filter by folderId Path
            if (!string.IsNullOrEmpty(folderIds) && folderIds != "-1")
            {
                var folderInfors = folderIds.Split(',');
                foreach (var folderInfor in folderInfors)
                {
                    var folderId = $"{folderInfor}/$";
                    filterFolder = Builders<Job>.Filter.Regex(x => x.DocPath, folderId);
                    if (filterFolders != null)
                    {
                        filterFolders = filterFolders | filterFolder;
                    }
                }
            }
            if (filterFolders != null)
            {
                baseFilter = baseFilter & filterFolders;
            }
            try
            {
                var lst = await _repository.FindAsync(baseFilter);
                if (lst.Count > 0)
                {
                    result = lst.GroupBy(x => new { x.DocInstanceId, x.WorkflowStepInstanceId }).Select(x =>
                        new ErrorDocReportSummary
                        {
                            InstanceId = x.Key.DocInstanceId.GetValueOrDefault(),
                            WorkflowStepInstanceId = x.Key.WorkflowStepInstanceId.GetValueOrDefault(),
                            ActionCode = x.First().ActionCode
                        }).ToList();

                    // Get workflow step name
                    var workflowStepInstanceIds = lst.Select(c => c.WorkflowStepInstanceId).ToList();
                    if (workflowStepInstanceIds.Count > 0)
                    {
                        workflowStepInstanceIds = workflowStepInstanceIds.Distinct().ToList();
                        string ids = JsonConvert.SerializeObject(workflowStepInstanceIds);
                        var wfsNames = await _workflowStepClientService.GetNameByInstanceIdsAsync(ids, accessToken);
                        if (wfsNames != null && wfsNames.Data != null && wfsNames.Data.Count() > 0 && workflowStepInstanceIds.Count > 0)
                        {
                            foreach (var item in result)
                            {
                                item.WorkflowStepName = wfsNames.Data.FirstOrDefault(c => c.InstanceId == item.WorkflowStepInstanceId)?.Name;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return GenericResponse<List<ErrorDocReportSummary>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return GenericResponse<List<ErrorDocReportSummary>>.ResultWithData(result);
        }

        public async Task<GenericResponse<PagedList<DocErrorDto>>> GetPagingErrorDocByProject(PagingRequest request, Guid projectInstanceId, string folderIds, string accessToken = null)
        {
            var pathFilters = request.Filters.Where(_ => _.Field != null && _.Field.Equals("slTreeFolder") && !string.IsNullOrWhiteSpace(_.Value)).ToList();
            // Filter by projectInstanceId
            var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Error);

            if (pathFilters.Count > 0)
            {
                var folderIdFilters = new List<FilterDefinition<Job>>();
                foreach (var pathFlter in pathFilters)
                {
                    folderIdFilters.Add(Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFlter.Value.ToString()}")));
                }
                if (folderIdFilters.Any())
                {
                    filter &= Builders<Job>.Filter.Or(folderIdFilters);
                }
            }

            // Filter by folderId
            if (!string.IsNullOrEmpty(folderIds))
            {
                if (folderIds.Contains(','))
                {
                    var folderIdFilters = new List<FilterDefinition<Job>>();
                    var lstFolderId = folderIds.Split(',');
                    foreach (var folderId in lstFolderId)
                    {
                        folderIdFilters.Add(Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{folderId}")));
                    }

                    if (folderIdFilters.Any())
                    {
                        filter &= Builders<Job>.Filter.Or(folderIdFilters);
                    }
                }
            }
            // Filter by ActionCode 
            var actionCodeFilterValue = request.Filters.Where(_ => _.Field != null && _.Field.Equals("ActionCode") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();

            if (actionCodeFilterValue != null)
            {
                filter &= Builders<Job>.Filter.Eq(x => x.ActionCode, actionCodeFilterValue.Value);
            }

            // Filter by DocName 
            var docNameFilterValue = request.Filters.Where(_ => _.Field != null && _.Field.Equals("DocName") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();

            if (docNameFilterValue != null)
            {
                filter &= Builders<Job>.Filter.Regex(x => x.DocName, new BsonRegularExpression(docNameFilterValue.Value, "i"));
            }

            var nullFilters = request.Filters.Where(_ => _.Field == null).ToList();
            if (nullFilters.Count > 0)
            {
                foreach (var ft in nullFilters)
                {
                    // Filter by DocName
                    var docNFilterValue = ft.Filters.Where(_ => _.Field != null && _.Field.Equals("DocName") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();

                    if (docNFilterValue != null)
                    {
                        filter &= Builders<Job>.Filter.Regex(x => x.DocName, new BsonRegularExpression(docNFilterValue.Value, "i"));
                    }
                }
            }

            if (request.PageInfo == null)
            {
                request.PageInfo = new PageInfo
                {
                    PageIndex = 1,
                    PageSize = 10
                };
            }

            var result = await _repository.GetPagingDocErrorAsync(filter, request.PageInfo.PageIndex, request.PageInfo.PageSize);

            // Get workflow step name
            var wfsInstanceIds = result.Data.Select(c => c.WorkflowStepInstanceId).Distinct().ToList();
            if (wfsInstanceIds.Any())
            {
                var wfsNameRs = await _workflowStepClientService.GetNameByInstanceIdsAsync(JsonConvert.SerializeObject(wfsInstanceIds), accessToken);

                if (wfsNameRs != null && wfsNameRs.Success && wfsNameRs.Data != null && wfsNameRs.Data.Any())
                {
                    foreach (var item in result.Data)
                    {
                        item.WorkflowStepName = wfsNameRs.Data.FirstOrDefault(c => c.InstanceId == item.WorkflowStepInstanceId)?.Name;
                    }
                }
            }

            var pagedList = new PagedList<DocErrorDto>
            {
                PageIndex = result.PageIndex,
                PageSize = result.PageSize,
                TotalCount = result.TotalCount,
                TotalFilter = result.TotalFilter,
                TotalPages = result.TotalPages,
                Data = _mapper.Map<List<DocErrorDto>>(result.Data)
            };

            return GenericResponse<PagedList<DocErrorDto>>.ResultWithData(pagedList);
        }

        public async Task<GenericResponse<bool>> RetryAllErrorDocs(Guid projectInstanceId, string accessToken)
        {
            var fitler = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Error);
            var findOption = new FindOptions<Job>()
            {
                BatchSize = 1000
            };
            var findJobCursor = await _repository.GetCursorListJobAsync(fitler, findOption);
            var jobs = new List<Job>();
            var totalError = 0;
            var totalSuccess = 0;
            while (findJobCursor.MoveNext())
            {
                jobs.AddRange(findJobCursor.Current);

                // 1. Mark doc, task processing & update progress statistic -> 2. Mark job processing -> 3. Send Event message
                foreach (var job in jobs)
                {
                    try
                    {
                        var inputParam = JsonConvert.DeserializeObject<InputParam>(job.Input);
                        List<WorkflowStepInfo> wfsInfoes = null;
                        if (inputParam.WorkflowStepInfoes == null)
                        {
                            var wfInfoes = await GetWfInfoes(inputParam.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                            wfsInfoes = wfInfoes.Item1;
                            inputParam.WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes);
                        }
                        wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);

                        var wfsInfo = wfsInfoes.FirstOrDefault(c => c.InstanceId == job.WorkflowStepInstanceId);
                        if (wfsInfo == null)
                        {
                            Log.Logger.Error($"RetryErrorDocs: Error get workflow step info with WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} !");
                        }

                        var resultDocChangeProcessingStatus = await _docClientService.ChangeStatus(job.DocInstanceId.GetValueOrDefault(), accessToken: accessToken);
                        if (!resultDocChangeProcessingStatus.Success)
                        {
                            var currentDoc = await _docClientService.GetByInstanceIdAsync(job.DocInstanceId.GetValueOrDefault(), accessToken);
                            if (currentDoc.Status != (short)EnumDoc.Status.Processing)
                            {
                                throw new Exception($"RetryErrorDocs: Error change doc status with DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} failure!");
                            }
                        }

                        var resultTaskChangeProcessingStatus = await _taskRepository.ChangeStatus(job.TaskId.ToString());
                        if (!resultTaskChangeProcessingStatus)
                        {
                            //kiểm tra thêm nếu có task.status = 2 thì ok => status !=2 => throw exception
                            var currentTask = await _taskRepository.GetByIdAsync(job.TaskId);
                            if (currentTask == null || currentTask.Status != (short)EnumTask.Status.Processing)
                            {
                                throw new Exception($"RetryErrorDocs: Error change task status with TaskId: {job.TaskId} failure!!");
                            }
                        }

                        var changeProjectFileProgress = new ProjectFileProgress
                        {
                            UnprocessedFile = -1,
                            ProcessingFile = 1,
                            CompleteFile = 0,
                            TotalFile = 0,
                            UnprocessedDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() },
                            ProcessingDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() }
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


                        // 2. Mark jobs processing
                        job.Status = (short)EnumJob.Status.Processing; //Todo: need refactor to set Status = Waiting instead of Processing
                        job.RetryCount += 1;


                        await _repos.UpdateAsync(job);

                        // Update current wfs status is processing
                        var resultDocChangeCurrentWfsInfoChild = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), wfsInfo.Id, (short)EnumJob.Status.Processing, wfsInfo.InstanceId, null, string.Empty, null, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfoChild.Success)
                        {
                            Log.Logger.Error($"{nameof(AfterProcessDataEntryBoolProcessEvent)}: Error change current work flow step info for DocInstanceId: {inputParam.DocInstanceId.GetValueOrDefault()} !");
                        }

                        // 3. Trigger retry docs
                        var evt = new RetryDocEvent
                        {
                            DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                            JobIds = new List<string>() { job.Id.ToString() }, //Todo: need refactor to user single jobId instead of list id
                            AccessToken = accessToken
                        };
                        await TriggerRetryDoc(evt, job.ActionCode);
                        totalSuccess += 1;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error when retry job id {job.Id.ToString()}");
                        totalError += 1;
                    }

                }

                jobs.Clear();

            }
            var result = true;
            string msg = $"Đã retry thành công {totalSuccess}";
            if (totalError > 0)
            {
                msg += $" - Không thành công: {totalError}";
            }

            if (totalSuccess <= 0)
            {
                result = false;
                msg = "Tổng số job bị lỗi khi retry: " + totalError.ToString();
            }
            return GenericResponse<bool>.ResultWithData(result, msg);

        }

        public async Task<GenericResponse<bool>> RetryErrorDocs(List<Guid> instanceIds, string accessToken)
        {
            List<Guid?> docInstanceIds = instanceIds.Select(x => (Guid?)x).ToList();
            var fitler = Builders<Job>.Filter.In(x => x.DocInstanceId, docInstanceIds) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Error);
            var findOption = new FindOptions<Job>()
            {
                BatchSize = 1000
            };
            var findJobCursor = await _repository.GetCursorListJobAsync(fitler, findOption);
            var jobs = new List<Job>();
            var totalError = 0;
            var totalSuccess = 0;
            while (findJobCursor.MoveNext())
            {
                //fetch job entity from mongo to memory
                jobs.AddRange(findJobCursor.Current);

                // 1. Mark doc, task processing & update progress statistic -> 2. Update job retry count -> 3. send event message
                foreach (var job in jobs)
                {
                    try
                    {
                        var inputParam = JsonConvert.DeserializeObject<InputParam>(job.Input);

                        List<WorkflowStepInfo> wfsInfoes = null;
                        if (inputParam.WorkflowStepInfoes == null)
                        {
                            var wfInfoes = await GetWfInfoes(inputParam.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                            wfsInfoes = wfInfoes.Item1;
                            inputParam.WorkflowStepInfoes = JsonConvert.SerializeObject(wfsInfoes);
                        }

                        wfsInfoes = JsonConvert.DeserializeObject<List<WorkflowStepInfo>>(inputParam.WorkflowStepInfoes);

                        var wfsInfo = wfsInfoes.FirstOrDefault(c => c.InstanceId == job.WorkflowStepInstanceId);
                        if (wfsInfo == null)
                        {
                            Log.Logger.Error($"RetryErrorDocs: Error get workflow step info with WorkflowStepInstanceId: {inputParam.WorkflowStepInstanceId} !");
                        }
                        var resultDocChangeProcessingStatus = await _docClientService.ChangeStatus(job.DocInstanceId.GetValueOrDefault(), accessToken: accessToken);
                        if (!resultDocChangeProcessingStatus.Success)
                        {
                            var currentDoc = await _docClientService.GetByInstanceIdAsync(job.DocInstanceId.GetValueOrDefault(), accessToken);
                            if (currentDoc.Status != (short)EnumDoc.Status.Processing)
                            {
                                throw new Exception($"RetryErrorDocs: Error change doc status with DocInstanceId: {job.DocInstanceId.GetValueOrDefault()} failure!");
                            }
                        }

                        var resultTaskChangeProcessingStatus = await _taskRepository.ChangeStatus(job.TaskId.ToString());
                        if (!resultTaskChangeProcessingStatus)
                        {
                            //kiểm tra thêm nếu có task.status = 2 thì ok => status !=2 => throw exception
                            var currentTask = await _taskRepository.GetByIdAsync(job.TaskId);
                            if (currentTask == null || currentTask.Status != (short)EnumTask.Status.Processing)
                            {
                                throw new Exception($"RetryErrorDocs: Error change task status with TaskId: {job.TaskId} failure!!");
                            }
                        }

                        var changeProjectFileProgress = new ProjectFileProgress
                        {
                            UnprocessedFile = -1,
                            ProcessingFile = 1,
                            CompleteFile = 0,
                            TotalFile = 0,
                            UnprocessedDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() },
                            ProcessingDocInstanceIds = new List<Guid> { job.DocInstanceId.GetValueOrDefault() }
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

                        // 2. Mark jobs processing
                        job.Status = (short)EnumJob.Status.Processing; //Todo: Cần refactor để chuyển status = Waiting -> đến lúc xử lý retry mới chuyển thành processing
                        job.RetryCount += 1;
                        await _repos.UpdateAsync(job);

                        // Update current wfs status is processing
                        var resultDocChangeCurrentWfsInfoChild = await _docClientService.ChangeCurrentWorkFlowStepInfo(inputParam.DocInstanceId.GetValueOrDefault(), wfsInfo.Id, (short)EnumJob.Status.Processing, wfsInfo.InstanceId, null, string.Empty, null, accessToken: accessToken);
                        if (!resultDocChangeCurrentWfsInfoChild.Success)
                        {
                            Log.Logger.Error($"{nameof(AfterProcessDataEntryBoolProcessEvent)}: Error change current work flow step info for DocInstanceId: {inputParam.DocInstanceId.GetValueOrDefault()} !");
                        }

                        // 3. Trigger retry docs
                        var evt = new RetryDocEvent
                        {
                            DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                            JobIds = new List<string> { job.Id.ToString() }, // Todo: Cần refactor chỗ này -> mỗi retry event chỉ cho 1 job vì các hàm handle chỉ xử lý 1 job cho 1 message
                            AccessToken = accessToken
                        };
                        await TriggerRetryDoc(evt, job.ActionCode);
                        totalSuccess += 1;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error when retry job id: {job.Id.ToString()}");
                        totalError += 1;
                    }
                }

                jobs.Clear();
            }
            var result = true;
            string msg = $"Đã retry thành công {totalSuccess}";
            if (totalError > 0)
            {
                msg += $" - Không thành công: {totalError}";
            }
            if (totalSuccess <= 0)
            {
                result = false;
                msg = "Tổng số job bị lỗi khi retry: " + totalError.ToString();
            }
            return GenericResponse<bool>.ResultWithData(result, msg);
        }

        #endregion

        #region GET VALUE CHART 
        public async Task<GenericResponse<SelectItemChartDto>> GetTimeNumberJobChart(string startDateStr, string endDateStr)
        {
            GenericResponse<SelectItemChartDto> response;
            try
            {
                if (_userPrincipalService.UserInstanceId != null && _userPrincipalService.UserInstanceId != Guid.Empty)
                {
                    var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId);
                    var filter2 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);// Lấy các job đã hoàn thành
                                                                                                         //StartDate
                    if (startDateStr != null)
                    {
                        var canParse = DateTimeOffset.Parse(startDateStr).UtcDateTime;
                        filter2 = filter2 & Builders<Job>.Filter.Gte(x => x.LastModificationDate, canParse.ToUniversalTime());
                    }

                    //endDate
                    if (endDateStr != null)
                    {
                        var canParse = DateTimeOffset.Parse(endDateStr).UtcDateTime;
                        filter2 = filter2 & Builders<Job>.Filter.Lt(x => x.LastModificationDate, canParse.ToUniversalTime());
                    }

                    var filter = filter1 & filter2;

                    var result = await _repository.GetTimeNumberJobChart(filter);
                    GenericResponse<SelectItemChartDto> model = new GenericResponse<SelectItemChartDto>();
                    model.Data = new SelectItemChartDto();
                    model.Data.Value = ((long)result);

                    return response = GenericResponse<SelectItemChartDto>.ResultWithData(model.Data);
                }
                response = GenericResponse<SelectItemChartDto>.ResultWithData(null);
            }
            catch (Exception ex)
            {
                response = GenericResponse<SelectItemChartDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Có lỗi sảy ra", "Có lỗi sảy ra");
                Log.Error(ex, ex.Message);
            }

            return response;
        }
        #endregion

        public async Task<GenericResponse<List<SummaryTotalDocPathJob>>> GetSummaryFolder(Guid projectInstanceId, string pathIds, string syncMetaPaths, string accessToken = null)
        {
            GenericResponse<List<SummaryTotalDocPathJob>> response;
            var result = new List<SummaryTotalDocPathJob>();
            try
            {
                var lstSyncMetaRelationAll = await _docClientService.GetAllSyncMetaRelationAsync(accessToken);
                var syncMetaRelations = lstSyncMetaRelationAll.Data;
                var syncMetaIds = !string.IsNullOrEmpty(pathIds) ? JsonConvert.DeserializeObject<List<string>>(pathIds) : new List<string>();
                var syncMetaPathsDeserilize = !string.IsNullOrEmpty(syncMetaPaths) ? JsonConvert.DeserializeObject<List<string>>(syncMetaPaths) : new List<string>();
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                if (syncMetaPathsDeserilize.Count() > 0)
                {
                    var regexFilters = syncMetaPathsDeserilize.Select(path => Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{path}"))).ToList();

                    var combinedFilter = Builders<Job>.Filter.And(filter, Builders<Job>.Filter.Or(regexFilters));

                    filter = combinedFilter;
                }
                var lstData = await _repository.GetSummaryFolder(filter);
                var lstDocInstanceId = lstData.Select(x => x.DocInstanceId).ToList();
                var lstDocDataResponse = await _docClientService.GetListDocByDocInstanceIds(lstDocInstanceId, accessToken);
                var lstDocData = lstDocDataResponse.Data;
                foreach (var data in lstData)
                {
                    var docData = lstDocData.FirstOrDefault(x => x.InstanceId == data.DocInstanceId);
                    if (docData != null)
                    {
                        data.RelationPath = docData.SyncMetaRelationPath;
                    }
                }
                foreach (var item in syncMetaIds)
                {
                    var id = item.Replace("/", "").Trim();
                    var syncMetaRelation = syncMetaRelations.FirstOrDefault(x => x.Id == long.Parse(id));
                    //var syncMetaRelationResponse = await _docClientService.GetSyncMetaRelationByIdAsync(long.Parse(item), accessToken);
                    //var syncMetaRelation = syncMetaRelationResponse.Data;
                    var data = lstData.Where(x => x.RelationPath != null && x.RelationPath.Contains(item)).ToList();
                    if (data == null) continue;
                    if (syncMetaRelation != null)
                    {
                        result.Add(new SummaryTotalDocPathJob
                        {
                            PathId = 0,
                            SyncMetaValuePath = "",
                            SyncMetaRelationPath = id,
                            PathRelation = syncMetaRelation.Path,
                            data = data
                        });
                    }
                    else
                    {
                        result.Add(new SummaryTotalDocPathJob
                        {
                            PathId = 0,
                            SyncMetaValuePath = "",
                            SyncMetaRelationPath = id,
                            PathRelation = "",
                            data = data
                        });
                    }
                }
                response = GenericResponse<List<SummaryTotalDocPathJob>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<SummaryTotalDocPathJob>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<List<SummaryTotalDocPathJob>>> GetSummaryFolder_old(Guid projectInstanceId, string lstPathId)
        {
            GenericResponse<List<SummaryTotalDocPathJob>> response;
            var result = new List<SummaryTotalDocPathJob>();
            try
            {
                var lstPathIdArr = !string.IsNullOrEmpty(lstPathId) ? JsonConvert.DeserializeObject<List<string>>(lstPathId) : new List<string>();
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var lstData = await _repository.GetSummaryFolder(filter);
                foreach (var item in lstPathIdArr)
                {
                    var data = lstData.Where(x => x.Path.Contains(item)).ToList();
                    result.Add(new SummaryTotalDocPathJob
                    {
                        PathId = 0,
                        SyncMetaValuePath = "",
                        PathRelation = item,
                        data = data
                    });
                }
                response = GenericResponse<List<SummaryTotalDocPathJob>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<SummaryTotalDocPathJob>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<List<TotalDocPathJob>>> GetSummaryDoc(Guid projectInstanceId, string path, string docInstanceIds)
        {
            GenericResponse<List<TotalDocPathJob>> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId) & Builders<Job>.Filter.Eq(x => x.DocPath, path);
                var lstDocInstanceId = JsonConvert.DeserializeObject<List<Guid?>>(docInstanceIds) ?? new List<Guid?>();
                if (lstDocInstanceId.Any())
                {
                    filter = filter & Builders<Job>.Filter.In(x => x.DocInstanceId, lstDocInstanceId);
                }
                var lstSummaryDoc = await _repository.GetSummaryDoc(filter);

                response = GenericResponse<List<TotalDocPathJob>>.ResultWithData(lstSummaryDoc);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<TotalDocPathJob>>.ResultWithError((short)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        #region Group by project
        public async Task<GenericResponse<List<ProjectCountExtensionDto>>> GetCountJobInProject(List<Guid?> projectInstanceIds, string strActionCode, string accessToken)
        {
            if (projectInstanceIds.Count == 0)
                return null;

            var actionCodes = new List<string>();
            if (!string.IsNullOrEmpty(strActionCode))
                actionCodes = JsonConvert.DeserializeObject<List<string>>(strActionCode);

            GenericResponse<List<ProjectCountExtensionDto>> response;
            try
            {
                var filter1 = Builders<Job>.Filter.In(x => x.ProjectInstanceId, projectInstanceIds);
                var filter2 = Builders<Job>.Filter.In(x => x.ActionCode, actionCodes);
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);// Lấy các job đang chờ xử lý

                var filter = filter1 & filter2 & filter3;

                var result = await _repository.GetCountJobInProject(filter);
                if (result.Count > 0)
                {
                    var data = _mapper.Map<List<ProjectCountExtension>, List<ProjectCountExtensionDto>>(result);
                    return response = GenericResponse<List<ProjectCountExtensionDto>>.ResultWithData(data);

                }

                response = GenericResponse<List<ProjectCountExtensionDto>>.ResultWithData(null);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<ProjectCountExtensionDto>>.ResultWithError((int)HttpStatusCode.BadRequest, "Có lỗi sảy ra", "Có lỗi sảy ra");
                Log.Error(ex, ex.Message);
            }
            return response;
        }

        #endregion


        #region Distribution job

        /// <summary>
        /// lấy danh sách các job đang đợi phân phối theo các tham số yêu cầu từ job distribution
        /// Huydq update: xử lý tự động bổ sung thêm các meta bị thiếu vào job nếu action = CheckFinal
        /// </summary>
        /// <param name="project"></param>
        /// <param name="actionCode"></param>
        /// <param name="inputType"></param>
        /// <param name="docTypeFieldInstanceId"></param>
        /// <param name="parallelInstanceIds"></param>
        /// <param name="docPath">Ưu tiên lấy theo docPath nào</param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<GenericResponse<List<JobDto>>> GetListJobForUser(ProjectDto project, string actionCode, Guid workflowStepInstanceId, int inputType, Guid docTypeFieldInstanceId, string parallelInstanceIds, string docPath, Guid batchInstanceId, int numOfRound, string accessToken = null)
        {
            var projectInstanceId = project.InstanceId;
            var projectTypeInstanceId = project.ProjectTypeInstanceId;

            GenericResponse<List<JobDto>> response;
            try
            {
                if (_userPrincipalService.UserInstanceId != null && _userPrincipalService.UserInstanceId != Guid.Empty)
                {
                    var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, null);
                    var filter2 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                    var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode); // ActionCode
                    var filter4 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);// Lấy các job đang đợi phân công

                    var filter = filter1 & filter2 & filter3;

                    if (projectTypeInstanceId != Guid.Empty)
                    {
                        filter = filter & Builders<Job>.Filter.Eq(x => x.ProjectTypeInstanceId, projectTypeInstanceId);
                    }

                    if (docTypeFieldInstanceId != Guid.Empty)
                    {
                        // inputType
                        filter = filter & Builders<Job>.Filter.Eq(x => x.InputType, inputType);

                        // docTypeFieldInstanceId
                        filter = filter & Builders<Job>.Filter.Eq(x => x.DocTypeFieldInstanceId, docTypeFieldInstanceId);
                    }

                    //uu tiên lấy theo docPath nếu có
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        filter &= Builders<Job>.Filter.Eq(x => x.DocPath, docPath);
                    }

                    //ưu tiên lấy theo lô
                    if (batchInstanceId != Guid.Empty)
                    {
                        filter &= Builders<Job>.Filter.Eq(x => x.BatchJobInstanceId, batchInstanceId);
                        filter &= Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                    }

                    //cần ưu tiên lấy các job CheckFinal bị trả về (numOfRound > 0) => cho phép lấy tất cả các phiếu có round >= numOfRound
                    if (actionCode == nameof(ActionCodeConstants.CheckFinal) && numOfRound > 0)
                    {
                        filter &= Builders<Job>.Filter.Eq(x => x.LastModifiedBy, _userPrincipalService.UserInstanceId);
                        filter &= Builders<Job>.Filter.Gte(x => x.NumOfRound, numOfRound);
                    }

                    var jobDtos = new List<JobDto>();

                    var workflowInstanceId = project.WorkflowInstanceId;
                    var tenantId = project.TenantId;

                    var jobs = new List<Job>();

                    var jobResult = new List<Job>();

                    var turnInstanceId = Guid.NewGuid();

                    var wfsInfo = project.ActionCodes.FirstOrDefault(c => c.ActionCode == actionCode && c.InstanceId == workflowStepInstanceId);

                    if (wfsInfo != null)
                    {
                        int pageSize = wfsInfo.ConfigStepProperty.NumOfJobDistributed > 0 ? wfsInfo.ConfigStepProperty.NumOfJobDistributed : 10;
                        var maxTimeProcessing = wfsInfo.ConfigStepProperty.MaxTimeProcessing;
                        var filterWfs = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wfsInfo.InstanceId);// Lấy các job theo workflowStepInstanceId tương ứng
                        var sortDefinition = Builders<Job>.Sort.Ascending(nameof(Job.CreatedDate)).Ascending(nameof(Job.LastModificationDate));

                        var parallelJob = wfsInfo.ActionCode == actionCode
                            && wfsInfo.ConfigStepProperty.IsShareJob
                            && wfsInfo.InputTypeGroups != null
                            && wfsInfo.InputTypeGroups.Any(x => x.InputType == inputType
                            && x.DocTypeFields != null
                            && x.DocTypeFields.Any(d => d.DocTypeFieldInstanceId == docTypeFieldInstanceId));

                        // Re calculate pagesize in case parallel job
                        var query = filter & filter4 & filterWfs;

                        if (parallelJob && wfsInfo.ConfigStepProperty.IsShareJob && wfsInfo.ConfigStepProperty.NumOfResourceInJob > 1)
                        {
                            int newPageSize = pageSize * wfsInfo.ConfigStepProperty.NumOfResourceInJob;
                            var searchResult = await _repository.GetAllJobAsync(query, sortDefinition, newPageSize);
                            // distinct job parallel
                            if (searchResult.Any())
                            {
                                var isNotParallelJob = searchResult.Where(c => !c.IsParallelJob).ToList();
                                var isParallelJob = searchResult.Where(c => c.IsParallelJob).ToList();
                                if (isParallelJob.Any())
                                {
                                    var distinctJob = isParallelJob.DistinctBy(c => c.ParallelJobInstanceId).ToList();
                                    isNotParallelJob.AddRange(distinctJob);
                                }
                                jobs = isNotParallelJob.OrderBy(c => c.CreatedDate).ThenBy(x => x.LastModificationDate).Take(pageSize).ToList();
                            }
                        }
                        else
                            jobs = await _repository.GetAllJobAsync(query, sortDefinition, pageSize);

                        //bổ sung các metadata còn thiếu nếu cần thiết cho các actionCode cụ thể
                        jobs = await AddMissedMetaDataField(jobs, actionCode, accessToken);

                        //bổ sung thêm các cấu hình trường thông tin
                        jobs = await AddDocTypeFieldExtraSetting(jobs, actionCode, accessToken);
                        if (jobs.Any())
                        {
                            var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct();
                            // Update current wfs status is processing
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(docInstanceIds), -1, (short)EnumJob.Status.Processing, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change multi current work flow step info for DocInstanceIds {JsonConvert.SerializeObject(docInstanceIds)} !");
                            }

                            var now = DateTime.UtcNow;
                            foreach (var job in jobs)
                            {
                                job.UserInstanceId = _userPrincipalService.UserInstanceId;
                                job.TurnInstanceId = turnInstanceId;
                                job.ReceivedDate = now;
                                job.DueDate = now.AddMinutes(maxTimeProcessing);
                                job.Status = (short)EnumJob.Status.Processing;
                                job.LastModificationDate = now;

                                var itemUpdate = await _repository.UpdateAndLockRecordAsync(job);
                                if (itemUpdate != null)
                                    jobResult.Add(job);
                            }
                        }

                        if (jobResult.Count > 0)
                        {
                            if (jobResult.Count > 0)
                            {
                                //SetCacheRedis
                                await SetCacheRecall(_userPrincipalService.UserInstanceId.Value, turnInstanceId, maxTimeProcessing, accessToken);

                                // Update asynchronous
                                await UpdateJob(jobResult, projectTypeInstanceId, projectInstanceId, workflowInstanceId, actionCode, wfsInfo.InstanceId, accessToken);
                            }

                            // Mapper
                            jobDtos = _mapper.Map<List<Job>, List<JobDto>>(jobResult);
                        }
                    }

                    response = GenericResponse<List<JobDto>>.ResultWithData(jobDtos);
                }
                else
                {
                    response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized.ToString(), "Chưa đăng nhập");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Lỗi nhận việc => {ex.Message} => {ex.StackTrace}");
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetListJobForUserByIds(ProjectDto project, string actionCode, Guid workflowStepInstanceId, string Ids, string accessToken = null)
        {
            var projectInstanceId = project.InstanceId;
            var projectTypeInstanceId = project.ProjectTypeInstanceId;

            GenericResponse<List<JobDto>> response;
            try
            {
                if (_userPrincipalService.UserInstanceId != null && _userPrincipalService.UserInstanceId != Guid.Empty)
                {
                    List<string> jobIdStrings = JsonConvert.DeserializeObject<List<string>>(Ids);
                    List<ObjectId> jobIdList = jobIdStrings.Select(id => ObjectId.Parse(id)).ToList();
                    var filter = Builders<Job>.Filter.In(x => x.Id, jobIdList);

                    var jobDtos = new List<JobDto>();

                    var workflowInstanceId = project.WorkflowInstanceId;
                    var tenantId = project.TenantId;

                    var jobs = new List<Job>();

                    var jobResult = new List<Job>();

                    var turnInstanceId = Guid.NewGuid();

                    var wfsInfo = project.ActionCodes.FirstOrDefault(c => c.ActionCode == actionCode && c.InstanceId == workflowStepInstanceId);

                    if (wfsInfo != null)
                    {
                        var maxTimeProcessing = wfsInfo.ConfigStepProperty.MaxTimeProcessing;

                        jobs = await _repository.FindAsync(filter);
                        //bổ sung các metadata còn thiếu nếu cần thiết cho các actionCode cụ thể
                        jobs = await AddMissedMetaDataField(jobs, actionCode, accessToken);

                        //bổ sung thêm các cấu hình trường thông tin
                        jobs = await AddDocTypeFieldExtraSetting(jobs, actionCode, accessToken);
                        if (jobs.Any())
                        {
                            var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct();
                            // Update current wfs status is processing
                            var resultDocChangeCurrentWfsInfo = await _docClientService.ChangeMultiCurrentWorkFlowStepInfo(JsonConvert.SerializeObject(docInstanceIds), -1, (short)EnumJob.Status.Processing, accessToken: accessToken);
                            if (!resultDocChangeCurrentWfsInfo.Success)
                            {
                                Log.Logger.Error($"{nameof(TaskProcessEvent)}: Error change multi current work flow step info for DocInstanceIds {JsonConvert.SerializeObject(docInstanceIds)} !");
                            }

                            var now = DateTime.UtcNow;
                            foreach (var job in jobs)
                            {
                                job.UserInstanceId = _userPrincipalService.UserInstanceId;
                                job.TurnInstanceId = turnInstanceId;
                                job.ReceivedDate = now;
                                job.DueDate = now.AddMinutes(maxTimeProcessing);
                                job.Status = (short)EnumJob.Status.Processing;
                                job.LastModificationDate = now;

                                var itemUpdate = await _repository.UpdateAndLockRecordAsync(job);
                                if (itemUpdate != null)
                                    jobResult.Add(job);
                            }
                        }

                        if (jobResult.Count > 0)
                        {
                            if (jobResult.Count > 0)
                            {
                                //SetCacheRedis
                                await SetCacheRecall(_userPrincipalService.UserInstanceId.Value, turnInstanceId, maxTimeProcessing, accessToken);

                                // Update asynchronous
                                await UpdateJob(jobResult, projectTypeInstanceId, projectInstanceId, workflowInstanceId, actionCode, wfsInfo.InstanceId, accessToken);
                            }

                            // Mapper
                            jobDtos = _mapper.Map<List<Job>, List<JobDto>>(jobResult);
                        }
                    }

                    response = GenericResponse<List<JobDto>>.ResultWithData(jobDtos);
                }
                else
                {
                    response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized.ToString(), "Chưa đăng nhập");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Lỗi nhận việc GetListJobForUserByIds: => {ex.Message} => {ex.StackTrace}");
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        /// <summary>
        /// Update trạng thái của Task, các thống kê liên quan khi phân phối job
        /// </summary>
        /// <param name="jobs"></param>
        /// <param name="projectTypeInstanceId"></param>
        /// <param name="projectInstanceId"></param>
        /// <param name="workflowInstanceId"></param>
        /// <param name="actionCode"></param>
        /// <param name="InstanceId"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private async Task UpdateJob(List<Job> jobs, Guid projectTypeInstanceId, Guid projectInstanceId, Guid? workflowInstanceId, string actionCode, Guid InstanceId, string accessToken)
        {
            // TaskStepProgress: Update value
            var wfInfoes = await GetWfInfoes(workflowInstanceId.GetValueOrDefault(), accessToken);
            var wfsInfoes = wfInfoes.Item1;
            var wfSchemaInfoes = wfInfoes.Item2;

            var wfsInfo = wfsInfoes.FirstOrDefault(c => c.InstanceId == InstanceId);
            var sortOrder = WorkflowHelper.GetOrderStep(wfsInfoes, wfsInfo.InstanceId);
            foreach (var job in jobs)
            {
                var updatedTaskStepProgress = new TaskStepProgress
                {
                    Id = wfsInfo.Id,
                    InstanceId = wfsInfo.InstanceId,
                    Name = wfsInfo.Name,
                    ActionCode = wfsInfo.ActionCode,
                    WaitingJob = -1,
                    ProcessingJob = 1,
                    CompleteJob = 0,
                    TotalJob = 0,
                    Status = (short)EnumTaskStepProgress.Status.Processing
                };
                var taskResult = await _taskRepository.UpdateProgressValue(job.TaskId.ToString(), updatedTaskStepProgress);
                if (taskResult != null)
                {
                    Log.Logger.Information($"TaskStepProgress: +1 ProcessingJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} success!");
                }
                else
                {
                    Log.Logger.Error($"TaskStepProgress: +1 ProcessingJob {job.ActionCode} in TaskInstanceId: {job.TaskInstanceId} with DocInstanceId: {job.DocInstanceId} failure!");
                }
            }

            // 2.1. Mark doc, task processing & update ProjectStatistic
            var crrWfsInfo = wfsInfo;
            var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId);
            var docInstanceIds = jobs.Where(x => x.DocInstanceId != null).Select(x => x.DocInstanceId.GetValueOrDefault()).Distinct();
            if (WorkflowHelper.IsMarkDocProcessing(wfsInfoes, wfSchemaInfoes, crrWfsInfo.InstanceId))
            {
                // Mark doc processing
                if (docInstanceIds.Any())
                {
                    string strDocInstanceIds = JsonConvert.SerializeObject(docInstanceIds);
                    await _docClientService.ChangeStatusMulti(strDocInstanceIds, accessToken: accessToken);
                }

                // Mark task processing
                var taskIds = jobs.Where(x => x.TaskId != ObjectId.Empty).Select(x => x.TaskId.ToString()).Distinct().ToList();
                if (taskIds.Any())
                {
                    var taskInstanceIds = jobs.Select(x => x.TaskInstanceId).Distinct().ToList();
                    var taskResults = await _taskRepository.ChangeStatusMulti(taskIds);
                    string msgProcessingTasks = taskInstanceIds.Count == 1 ? "ProcessingTask" : "ProcessingTasks";
                    string msgTaskInstanceIds = taskInstanceIds.Count == 1 ? "TaskInstanceId" : "TaskInstanceIds";
                    if (taskResults)
                    {
                        Log.Logger.Information($"TaskStepProgress: +{taskInstanceIds.Count} {msgProcessingTasks} in {msgTaskInstanceIds}: {string.Join(',', taskInstanceIds)} success!");
                    }
                    else
                    {
                        Log.Logger.Error($"TaskStepProgress: +{taskInstanceIds.Count} {msgProcessingTasks} in {msgTaskInstanceIds}: {string.Join(',', taskInstanceIds)} failure!");
                    }
                }
            }

            // ProjectStatistic: Update
            if (docInstanceIds.Any())
            {
                var changeProjectStatisticMulti = new ProjectStatisticUpdateMultiProgressDto
                {
                    ItemProjectStatisticUpdateProgresses = new List<ItemProjectStatisticUpdateProgressDto>(),
                    ProjectTypeInstanceId = projectTypeInstanceId,
                    ProjectInstanceId = projectInstanceId,
                    WorkflowInstanceId = workflowInstanceId,
                    WorkflowStepInstanceId = wfsInfo.InstanceId,
                    ActionCode = actionCode,
                    DocInstanceIds = JsonConvert.SerializeObject(docInstanceIds),
                    TenantId = jobs.First().TenantId
                };

                int countOfProcessingFileInFileStatistic = 0;
                var processingDocInstanceIdsInFileStatistic = new List<Guid>();
                int countOfProcessingFileInStepStatistic = 0;
                var processingDocInstanceIdsStepStatistic = new List<Guid>();
                foreach (var docInstanceId in docInstanceIds)
                {
                    ProjectFileProgress changeProjectFileProgress;
                    if (prevWfsInfoes.Count == 1 && prevWfsInfoes.FirstOrDefault(x => x.ActionCode == ActionCodeConstants.Upload) != null)
                    {
                        changeProjectFileProgress = new ProjectFileProgress
                        {
                            UnprocessedFile = -1,
                            ProcessingFile = 1,
                            CompleteFile = 0,
                            TotalFile = 0,
                            UnprocessedDocInstanceIds = new List<Guid> { docInstanceId },
                            ProcessingDocInstanceIds = new List<Guid> { docInstanceId }
                        };
                        countOfProcessingFileInFileStatistic++;
                        processingDocInstanceIdsInFileStatistic.Add(docInstanceId);
                    }
                    else
                    {
                        changeProjectFileProgress = new ProjectFileProgress
                        {
                            UnprocessedFile = 0,
                            ProcessingFile = 0,
                            CompleteFile = 0,
                            TotalFile = 0
                        };
                    }

                    var crrJobs = jobs.Where(x => x.DocInstanceId == docInstanceId).ToList();
                    var changeProjectStepProgress = new List<ProjectStepProgress>();
                    // Nếu tồn tại job Complete thì ko chuyển trạng thái về Processing
                    var hasJobComplete =
                        await _repository.CheckHasJobCompleteByWfs(docInstanceId, actionCode, crrWfsInfo.InstanceId);
                    if (!hasJobComplete)
                    {
                        changeProjectStepProgress = crrJobs.GroupBy(x => new { x.ProjectInstanceId, x.WorkflowInstanceId, x.WorkflowStepInstanceId, x.ActionCode }).Select(grp => new ProjectStepProgress
                        {
                            InstanceId = grp.Key.WorkflowStepInstanceId.GetValueOrDefault(),
                            Name = string.Empty,
                            ActionCode = grp.Key.ActionCode,
                            ProcessingFile = grp.Select(i => i.DocInstanceId.GetValueOrDefault()).Distinct().Count(),
                            CompleteFile = 0,
                            TotalFile = 0,
                            ProcessingDocInstanceIds = new List<Guid> { docInstanceId }
                        }).ToList();
                        countOfProcessingFileInStepStatistic += changeProjectStepProgress.Sum(s => s.ProcessingFile);
                        processingDocInstanceIdsStepStatistic.Add(docInstanceId);
                    }

                    changeProjectStatisticMulti.ItemProjectStatisticUpdateProgresses.Add(new ItemProjectStatisticUpdateProgressDto
                    {
                        DocInstanceId = docInstanceId,
                        StatisticDate = Int32.Parse(crrJobs.First().DocCreatedDate.GetValueOrDefault().Date.ToString("yyyyMMdd")),
                        ChangeFileProgressStatistic = JsonConvert.SerializeObject(changeProjectFileProgress),
                        ChangeStepProgressStatistic = JsonConvert.SerializeObject(changeProjectStepProgress),
                        ChangeUserStatistic = string.Empty  // Worker phải hoàn thành công việc thì mới đc thống kê vào dự án
                    });
                }

                if (countOfProcessingFileInFileStatistic > 0 || countOfProcessingFileInStepStatistic > 0)
                {
                    await _projectStatisticClientService.UpdateMultiProjectStatisticAsync(changeProjectStatisticMulti, accessToken);

                    string msgProcessingFilesInFileStatistic = countOfProcessingFileInFileStatistic == 1 ? "ProcessingFile" : "ProcessingFiles";
                    string msgDocInstanceIdsInFileStatistic = countOfProcessingFileInFileStatistic == 1 ? "DocInstanceId" : "DocInstanceIds";
                    string msgTaskInstanceIdsInStepStatistic = countOfProcessingFileInStepStatistic == 1 ? "ProcessingFile" : "ProcessingFiles";
                    string msgDocInstanceIdsInStepStatistic = countOfProcessingFileInStepStatistic == 1 ? "DocInstanceId" : "DocInstanceIds";
                    string msgFileStatistic = countOfProcessingFileInFileStatistic > 0
                        ? $"+{countOfProcessingFileInFileStatistic} {msgProcessingFilesInFileStatistic} for FileProgressStatistic with {msgDocInstanceIdsInFileStatistic}: {string.Join(',', processingDocInstanceIdsInFileStatistic)}, "
                        : "";
                    string msgStepStatistic = countOfProcessingFileInStepStatistic > 0
                        ? $"+{countOfProcessingFileInStepStatistic} {msgTaskInstanceIdsInStepStatistic} for StepProgressStatistic with {msgDocInstanceIdsInStepStatistic}: {string.Join(',', processingDocInstanceIdsStepStatistic)}"
                        : "";
                    string message = $"Published {nameof(ProjectStatisticUpdateMultiProgressEvent)}: ProjectStatistic: {msgFileStatistic}{msgStepStatistic}";
                    Log.Logger.Information(message);
                }
            }
        }
        #endregion

        public async Task<GenericResponse<PagedListExtension<JobProcessingStatistics>>> GetTotalJobProcessingStatistics_V2(PagingRequest request, bool hasPaging = true)
        {
            GenericResponse<PagedListExtension<JobProcessingStatistics>> response;
            try
            {
                if (request.PageInfo == null)
                {
                    request.PageInfo = new PageInfo
                    {
                        PageIndex = 1,
                        PageSize = 10
                    };
                }

                if (request.PageInfo.PageIndex <= 0)
                {
                    return GenericResponse<PagedListExtension<JobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Page index must be greater or than 1");
                }
                if (request.PageInfo.PageSize < 0)
                {
                    return GenericResponse<PagedListExtension<JobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Page size must be greater or than 0");
                }

                var projectInstanceId = Guid.Empty;
                PagedListExtension<JobProcessingStatistics> result = new PagedListExtension<JobProcessingStatistics>();

                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete)
                    & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                var lastFilter = baseFilter;
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse)
                        {
                            var pareDate = new DateTime(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0);
                            lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, pareDate.ToUniversalTime());
                        }
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse)
                        {
                            endDate = endDate.AddDays(1);
                            var pareDate = new DateTime(endDate.Year, endDate.Month, endDate.Day, 23, 59, 59);
                            lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                        }
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                        projectInstanceId = Guid.Parse(projectInstanceIdFilter.Value);
                    }
                }
                if (projectInstanceId == Guid.Empty || projectInstanceId == null)
                {
                    return GenericResponse<PagedListExtension<JobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Không lấy được thông tin dự án");
                }

                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId)
                    & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                var lstUerInstanceId = await _repository.GetDistinctUserInstanceId(filter);
                if (lstUerInstanceId.Any())
                {
                    var lstUserFilter = lstUerInstanceId.OrderBy(x => x).ToList();

                    var lstUserCovert = lstUserFilter.ConvertAll<Guid?>(i => i).ToList();
                    lastFilter = lastFilter & Builders<Job>.Filter.In(x => x.UserInstanceId, lstUserCovert);
                    var data = await _repository.GetTotalJobProcessingStatistics_V2(lastFilter);
                    var lstWfStepInstanceId = data.Select(x => x.WorkflowStepInstanceId).Distinct();
                    var lstJobProcessingStatistics = new List<JobProcessingStatistics>();
                    foreach (var wfStep in lstWfStepInstanceId)
                    {
                        lstJobProcessingStatistics.Add(new JobProcessingStatistics
                        {
                            WorkflowStepInstanceId = wfStep,
                            Total = data.Where(x => x.WorkflowStepInstanceId == wfStep).Select(z => z.Total).Sum(),
                            Total_Correct = data.Where(x => x.WorkflowStepInstanceId == wfStep && x.ActionCode == nameof(ActionCodeConstants.DataCheck)).Select(z => z.Total_Correct).Sum(),
                            Total_Ignore = data.Where(x => x.WorkflowStepInstanceId == wfStep && x.ActionCode == nameof(ActionCodeConstants.DataEntry)).Select(z => z.Total_Ignore).Sum(),
                            Total_Wrong = data.Where(x => x.WorkflowStepInstanceId == wfStep && x.ActionCode == nameof(ActionCodeConstants.DataCheck)).Select(z => z.Total_Wrong).Sum()
                        });
                    }
                    var totalJobProcessingStatistics = new JobProcessingStatistics
                    {
                        WorkflowStepInstanceId = new Guid()
                    };
                    if (hasPaging)
                    {
                        var lstUserPaging = lstUserFilter.Skip((request.PageInfo.PageIndex - 1) * request.PageInfo.PageSize).Take(request.PageInfo.PageSize).ToList();
                        var lstUserPagingCovert = lstUserPaging.ConvertAll<Guid?>(i => i).ToList();
                        data = data.Where(x => lstUserPagingCovert.Contains(x.UserInstanceId)).ToList();
                    }
                    result = new PagedListExtension<JobProcessingStatistics>
                    {
                        Data = data,
                        PageIndex = request.PageInfo.PageIndex,
                        PageSize = request.PageInfo.PageSize,
                        TotalCount = lstUerInstanceId.Count(),
                        TotalFilter = lstUerInstanceId.Count(),
                        TotalPages = (int)Math.Ceiling((decimal)lstUerInstanceId.Count() / request.PageInfo.PageSize),
                        lstJobProcessingStatistics = lstJobProcessingStatistics
                    };
                }

                response = GenericResponse<PagedListExtension<JobProcessingStatistics>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<PagedListExtension<JobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<TotalJobProcessingStatistics>>> GetTotalJobProcessingStatistics(Guid projectInstanceId, string startDate = null, string endDate = null)
        {
            GenericResponse<List<TotalJobProcessingStatistics>> response;
            var result = new List<TotalJobProcessingStatistics>();
            try
            {
                DateTime? pareStartDate = null;
                DateTime? pareEndDate = null;

                if (!string.IsNullOrEmpty(startDate))
                {
                    var canParse = DateTime.TryParse(startDate, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime resultDate);
                    if (canParse) pareStartDate = resultDate.ToUniversalTime(); //new DateTime(now.Year, now.Month, now.Day, 0, 0, 0);
                }
                if (!string.IsNullOrEmpty(endDate))
                {
                    var canParse = DateTime.TryParse(endDate, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime resultDate);
                    if (canParse) pareEndDate = resultDate.AddDays(1).ToUniversalTime();

                }

                var filter1 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter2 = Builders<Job>.Filter.Nin(x => x.ActionCode, new List<string> { "Ocr", "Crop" });
                var filter3 = Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                var filter4 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);
                var filter5 = Builders<Job>.Filter.Ne(x => x.LastModificationDate, null);
                var filter = filter1 & filter2 & filter3 & filter4 & filter5;
                if (pareStartDate.HasValue)
                {
                    filter = filter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, new DateTime(pareStartDate.Value.Year, pareStartDate.Value.Month, pareStartDate.Value.Day, 0, 0, 0));
                }
                if (pareEndDate.HasValue)
                {
                    filter = filter & Builders<Job>.Filter.Lte(x => x.LastModificationDate, new DateTime(pareEndDate.Value.Year, pareEndDate.Value.Month, pareEndDate.Value.Day, 23, 59, 59));
                }
                result = await _repository.GetTotalJobProcessingStatistics(filter);

                response = GenericResponse<List<TotalJobProcessingStatistics>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<TotalJobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<TotalJobProcessingStatistics>>> GetTotalJobPaymentStatistics(Guid projectInstanceId)
        {
            GenericResponse<List<TotalJobProcessingStatistics>> response;
            var result = new List<TotalJobProcessingStatistics>();
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter1 = Builders<Job>.Filter.Nin(x => x.ActionCode, new List<string> { "Ocr", "Crop" });
                result = await _repository.TotalJobPaymentStatistics(filter & filter1);

                response = GenericResponse<List<TotalJobProcessingStatistics>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<TotalJobProcessingStatistics>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        /// <summary>
        /// Lock job
        /// </summary>
        /// <param name="projectInstanceId"></param>
        /// <param name="pathRelationId">Path Id bảng SyncMetaRelation</param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<GenericResponse<bool>> LockJobByPath(Guid projectInstanceId, string pathRelationId, string accessToken = null)
        {
            GenericResponse<bool> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter2 = Builders<Job>.Filter.Regex(x => x.DocPath, "^" + pathRelationId);
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);

                var data = await _repos.FindAsync(filter & filter2 & filter3);
                if (data != null && data.Any())
                {
                    foreach (var item in data)
                    {
                        item.Status = (short)EnumJob.Status.Locked;
                    }
                    var result = await _repository.UpdateMultiAsync(data);
                    response = GenericResponse<bool>.ResultWithData(result > 0);
                }
                else
                {
                    response = GenericResponse<bool>.ResultWithData(false);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }


        public async Task<GenericResponse<bool>> UnLockJobByPath(Guid projectInstanceId, string pathRelationId, string accessToken = null)
        {
            GenericResponse<bool> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
                var filter2 = Builders<Job>.Filter.Regex(x => x.DocPath, "^" + pathRelationId);
                var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Locked);

                var data = await _repos.FindAsync(filter & filter2 & filter3);
                if (data != null && data.Any())
                {
                    foreach (var item in data)
                    {
                        item.Status = (short)EnumJob.Status.Waiting;
                    }
                    var result = await _repository.UpdateMultiAsync(data);
                    response = GenericResponse<bool>.ResultWithData(result > 0);
                }
                else
                {
                    response = GenericResponse<bool>.ResultWithData(false);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<CountJobEntity>>> GetCountAllJobByStatus()
        {
            try
            {
                var data = await _repository.GetCountAllJobByStatus();
                if (data != null && data.Any())
                {
                    int total = data.Select(x => x.Total).Sum();
                    foreach (var item in data)
                    {
                        var enumValue = item.Status;
                        var descriptionAttribute = enumValue.GetType()
                            .GetField(enumValue.ToString())
                            .GetCustomAttributes(false)
                            .SingleOrDefault(attr => attr.GetType() == typeof(System.ComponentModel.DescriptionAttribute)) as System.ComponentModel.DescriptionAttribute;
                        item.StatusName = descriptionAttribute?.Description ?? "";

                        var p = total > 0 ? (decimal)item.Total * 100 / (decimal)total : 0;
                        item.Percent = (int)Math.Round(p, MidpointRounding.ToEven);
                    }
                }

                return GenericResponse<List<CountJobEntity>>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<CountJobEntity>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<List<CountJobEntity>>> GetSummaryJobByAction(Guid projectInstanceId, string fromDate, string toDate)
        {
            try
            {
                var data = await _repository.GetSummaryJobByAction(projectInstanceId, fromDate, toDate);
                if (data != null && data.Any())
                {
                    foreach (var item in data)
                    {
                        var p = item.Total > 0 ? (decimal)item.Complete * 100 / (decimal)item.Total : 0;
                        item.Percent = (int)Math.Round(p, MidpointRounding.ToEven);
                    }
                }

                return GenericResponse<List<CountJobEntity>>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<CountJobEntity>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }
        /// <summary>
        /// Thống kê số lượng job theo từng step
        /// Được call từ web menu: Thống kê dự án
        /// </summary>
        /// <param name="projectInstanceId"></param>
        /// <returns></returns>
        public async Task<GenericResponse<List<CountJobEntity>>> GetSummaryJobCompleteByAction(Guid projectInstanceId)
        {
            try
            {
                var data = await _repository.GetSummaryJobCompleteByAction(projectInstanceId);
                if (data != null && data.Any())
                {
                    foreach (var item in data)
                    {
                        var p = item.Total > 0 ? (decimal)item.Complete * 100 / (decimal)item.Total : 0;
                        item.Percent = (int)Math.Round(p, MidpointRounding.ToEven);
                    }
                }

                return GenericResponse<List<CountJobEntity>>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<CountJobEntity>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<WorkSpeedReportEntity>> GetWorkSpeed(Guid? projectInstanceId, Guid? userInstanceId)
        {
            try
            {
                var data = await _repository.GetWorkSpeed(projectInstanceId, userInstanceId);
                return GenericResponse<WorkSpeedReportEntity>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<WorkSpeedReportEntity>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<List<JobByDocDoneEntity>>> GetSummaryJobOfDoneFileByStep(Guid? projectInstanceId, string lastAction)
        {
            try
            {
                var data = await _repository.GetSummaryJobOfDoneFileByStep(projectInstanceId, lastAction);
                return GenericResponse<List<JobByDocDoneEntity>>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<JobByDocDoneEntity>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }

        public async Task<GenericResponse<List<JobOfFileEntity>>> GetSummaryJobOfFile(Guid? docInstanceId)
        {
            try
            {
                var data = await _repository.GetSummaryJobOfFile(docInstanceId);
                return GenericResponse<List<JobOfFileEntity>>.ResultWithData(data);
            }
            catch (Exception ex)
            {
                return GenericResponse<List<JobOfFileEntity>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
        }
    }

    /// <summary>
    /// Private methods
    /// </summary>
    public partial class JobService
    {


        private async Task SetCacheRecall(Guid userInstanceId, Guid turnInstanceId, int minutesExpired, string accessToken)
        {
            string cacheKey = $"$@${userInstanceId}$@${turnInstanceId}$@${accessToken}$@$RecallJob";
            await _cachingHelper.TrySetCacheAsync<int>(cacheKey, 1, minutesExpired * 60);
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

        private async Task TriggerRetryDoc(RetryDocEvent evt, string retryWfsActionCode)
        {
            bool isCrrStepHeavyJob = WorkflowHelper.IsHeavyJob(retryWfsActionCode);
            //bool isCrrStepHeavyJob = true;

            // Outbox
            var exchangeName = isCrrStepHeavyJob ? RabbitMqExchangeConstants.EXCHANGE_HEAVY_RETRY_DOC : nameof(RetryDocEvent).ToLower();
            var outboxEntity = new OutboxIntegrationEvent
            {
                ExchangeName = exchangeName,
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(evt),
                LastModificationDate = DateTime.UtcNow,
                Status = (short)EnumEventBus.PublishMessageStatus.Nack
            };

            try
            {
                _eventBus.Publish(evt, exchangeName);
            }
            catch (Exception exPublishEvent)
            {
                Log.Error(exPublishEvent, $"Error publish for event {exchangeName}");
                try
                {
                    await _outboxIntegrationEventRepository.AddAsync(outboxEntity);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error save DB for event {exchangeName}");
                    throw;
                }
            }

        }

        private bool IsValidCheckFinalValue(List<StoredDocItem> oldValue, List<StoredDocItem> newValue)
        {
            if (oldValue == null || oldValue.Count == 0 || newValue == null || newValue.Count == 0)
            {
                return false;
            }
            if (oldValue.Count != newValue.Count)
            {
                return false;
            }
            var lstDocTypeFieldInstanceId = oldValue.Select(x => x.DocTypeFieldInstanceId).ToList();
            var lstDocTypeFieldInstanceIdNew = newValue.Select(x => x.DocTypeFieldInstanceId).ToList();

            bool isEqual = Enumerable.SequenceEqual(lstDocTypeFieldInstanceId.OrderBy(x => x), lstDocTypeFieldInstanceIdNew.OrderBy(y => y));
            return isEqual;
        }

        public async Task<GenericResponse<JobDto>> GetCompleteJobById(string id)
        {
            GenericResponse<JobDto> response;
            if (!ObjectId.TryParse(id, out ObjectId jobId))
            {
                response = GenericResponse<JobDto>.ResultWithData(null, "Mã công việc không chính xác");
                return response;
            }

            var filterId = Builders<Job>.Filter.Eq(x => x.Id, jobId);
            var filterUser = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId);
            //var filterStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);// Lấy các job đã xử lý xong

            var job = await _repository.FindFirstAsync(filterId & filterUser/* & filterStatus*/);
            var result = _mapper.Map<Job, JobDto>(job);
            response = GenericResponse<JobDto>.ResultWithData(result);
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetListCompleteJobByFilePartInstanceId(string strfilePartInstanceId)
        {
            if (string.IsNullOrEmpty(strfilePartInstanceId))
            {
                return GenericResponse<List<JobDto>>.ResultWithData(null);
            }
            Guid filePartInstanceId = new Guid(strfilePartInstanceId);

            GenericResponse<List<JobDto>> response;
            try
            {
                var filterId = Builders<Job>.Filter.Eq(x => x.FilePartInstanceId, filePartInstanceId);
                var filterUser = Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId);
                var filterStatus = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);// Lấy các job đã xử lý xong

                var jobs = await _repos.FindAsync(filterId & filterUser & filterStatus);

                var result = _mapper.Map<List<Job>, List<JobDto>>(jobs);
                response = GenericResponse<List<JobDto>>.ResultWithData(result);
                return response;
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        private async Task<ConfirmAutoOutputDto> GetConfirmAutoResult(ConfirmAutoInputDto model, string accessToken)
        {
            try
            {
                var providerServiceConfigResponse = await _providerConfig.GetPrimaryConfigForActionCode(ActionCodeConstants.DataConfirmAuto, null, accessToken);
                if (providerServiceConfigResponse == null || !providerServiceConfigResponse.Success || providerServiceConfigResponse.Data == null)
                {
                    throw new Exception("Can't get configured ExternalProviderServiceConfig!");
                }
                var providerServiceConfig = providerServiceConfigResponse.Data;
                var client = _clientFatory.Create();
                var headers = new Dictionary<string, string>();
                var headerConfigs = JsonConvert.DeserializeObject<List<RequestHeaderConfigDto>>(providerServiceConfig.ConfigHeader);
                foreach (var item in headerConfigs)
                {
                    if (!headers.ContainsKey(item.Key))
                    {
                        headers.Add(item.Key, item.Value);
                    }
                }
                if (!headers.ContainsKey("X-Request-Id"))
                {
                    headers.Add("X-Request-Id", Guid.NewGuid().ToString());
                }
                var response = await client.PostAsync<ConfirmAutoOutputDto>(providerServiceConfig.Domain, providerServiceConfig.ApiEndpoint, model, null, headers, null, AccessTokenType.Basic, true);

                return response;
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger.Error("GetConfirmAutoResult: StackTrace => " + ex.StackTrace);
                Serilog.Log.Logger.Error("GetConfirmAutoResult: InnerException => " + ex.InnerException);
                Serilog.Log.Logger.Error("GetConfirmAutoResult: Message => " + ex.Message);
                return null;
            }
        }

        public async Task<GenericResponse<int>> SkipJobDataCheck(string jobIdstr, string reason)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdstr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");
                return response;
            }

            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);
            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");
                return response;
            }

            if (job.IsIgnore)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đã bỏ qua");
                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.DataCheck))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc kiểm tra");
                return response;
            }

            var updateSkip = Builders<Job>.Update
               .Set(s => s.ReasonIgnore, reason)
               .Set(s => s.LastModificationDate, DateTime.UtcNow)
               .Set(s => s.IsIgnore, true)
               .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var rs = await _repository.UpdateOneAsync(filter, updateSkip);

            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<GenericResponse<int>> UndoSkipJobDataCheck(string jobIdStr)
        {
            GenericResponse<int> response;
            if (!ObjectId.TryParse(jobIdStr, out ObjectId jobId))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Mã công việc không chính xác");
                return response;
            }
            var filter = Builders<Job>.Filter.Eq(x => x.Id, jobId); // lấy theo id


            var job = await _repository.FindFirstAsync(filter);
            if (job == null)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc không tồn tại");
                return response;
            }

            if (!job.IsIgnore)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc chưa bỏ qua");
                return response;
            }

            if (job.Status != (short)EnumJob.Status.Processing)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Việc đang không được nhận");
                return response;
            }

            if (job.UserInstanceId != _userPrincipalService.UserInstanceId)
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không có quyền");
                return response;
            }

            if (job.ActionCode != nameof(ActionCodeConstants.DataCheck))
            {
                response = GenericResponse<int>.ResultWithData(-1, "Không phải việc kiểm tra");
                return response;
            }

            var updateSkip = Builders<Job>.Update
               .Set(s => s.ReasonIgnore, null)
               .Set(s => s.LastModificationDate, DateTime.UtcNow)
               .Set(s => s.IsIgnore, false)
               .Set(s => s.LastModifiedBy, _userPrincipalService.UserInstanceId);

            var rs = await _repository.UpdateOneAsync(filter, updateSkip);

            response = GenericResponse<int>.ResultWithData(1);
            return response;
        }

        public async Task<bool> PublishLogJobEvent(List<Job> jobs, string accessToken)
        {
            // Publish message sang DistributionJob
            var logJobEvt = new LogJobEvent
            {
                LogJobs = _mapper.Map<List<Job>, List<LogJobDto>>(jobs),
                AccessToken = accessToken
            };
            // Outbox
            var isAckLogJobEvent = false;
            var outboxEntityLogJobEvent = new OutboxIntegrationEvent
            {
                ExchangeName = nameof(LogJobEvent).ToLower(),
                ServiceCode = _configuration.GetValue("ServiceCode", string.Empty),
                Data = JsonConvert.SerializeObject(logJobEvt),
                LastModificationDate = DateTime.UtcNow,
                Status = (short)EnumEventBus.PublishMessageStatus.Nack
            };
            try
            {
                isAckLogJobEvent = _eventBus.Publish(logJobEvt, nameof(LogJobEvent).ToLower());
            }
            catch (Exception exPublishEvent)
            {
                Log.Error(exPublishEvent, "Error publish for event LogJobEvent");
                try
                {
                    await _outboxIntegrationEventRepository.AddAsync(outboxEntityLogJobEvent);
                }
                catch (Exception exSaveDB)
                {
                    Log.Error(exSaveDB, "Error save DB for evnet LogJobEvent");
                }
            }

            return isAckLogJobEvent;
        }

        /// <summary>
        /// Bổ sung các meta data chưa vào OldValue -> và gán Value=OldValue
        ///     Hàm này chỉ dùng cho để xử lý các job đang được lấy ra để phân phối cho nhân sự xử lý
        ///         Tại các công việc cần hiện thị đầy đủ meta như CheckFinal
        /// Hàm này có thể trong tương lai không cần nữa
        ///     Còn hiện tại thì cần để xử lý cho các workflow đi thẳng từ OCR (hoặc Upload) -> CheckFinal
        /// </summary>
        /// <param name="jobs"></param>
        /// <returns></returns>
        private async Task<List<Job>> AddMissedMetaDataField(List<Job> jobs, string actionCode, string accessToken)
        {
            var updatedJobs = jobs;

            var isValidActionCode = false;
            //lọc ra các action code
            if (actionCode == ActionCodeConstants.CheckFinal)
            {
                isValidActionCode = true;
            }

            if (!isValidActionCode || !jobs.Any())
            {
                return updatedJobs;
            }

            //Bắt đầu tiến trình xử lý: tìm metadata còn thiếu trong value của job so với bộ meta đầy đủ của loại tài liệu
            // lấy danh sách bộ Field cho từng loại tài liệu -> với mỗi job -> kiểm tra xem trong value còn thiếu Field nào  còn thiếu trường nào thì bổ sung
            var tempDictionaryDocItemByTemplate = new Dictionary<Guid, List<DocItem>>();
            var lstDocItemFull = new List<GroupDocItem>();

            //lấy danh sách các bộ metadata field cho từng loại tài liệu
            foreach (var job in updatedJobs)
            {
                //lấy danh sách DocItem theo mẫu số hóa 
                if (!tempDictionaryDocItemByTemplate.ContainsKey(job.DigitizedTemplateInstanceId.Value))
                {
                    var listDocTypeFieldRs = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(job.ProjectInstanceId.GetValueOrDefault(), job.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                    if (listDocTypeFieldRs.Success && listDocTypeFieldRs.Data.Any())
                    {
                        var listDocItemByTemplate = _docTypeFieldClientService.ConvertToDocItem(listDocTypeFieldRs.Data);
                        tempDictionaryDocItemByTemplate.Add(job.DigitizedTemplateInstanceId.Value, listDocItemByTemplate);
                    }
                }

                if (tempDictionaryDocItemByTemplate.ContainsKey(job.DigitizedTemplateInstanceId.Value))
                {
                    var listDocItem = tempDictionaryDocItemByTemplate[job.DigitizedTemplateInstanceId.Value];
                    lstDocItemFull.Add(new GroupDocItem()
                    {
                        DocInstanceId = job.DocInstanceId.GetValueOrDefault(),
                        DocItems = listDocItem
                    });
                }
            }

            //duyệt từng job -> kiểm tra trong value nếu thiếu thì bổ sung
            foreach (var job in updatedJobs)
            {
                if (string.IsNullOrEmpty(job.Value))
                {
                    job.Value = JsonConvert.SerializeObject(new List<DocItem>());
                }

                if (string.IsNullOrEmpty(job.OldValue))
                {
                    job.OldValue = JsonConvert.SerializeObject(new List<DocItem>());
                }


                var jobValue = JsonConvert.DeserializeObject<List<DocItem>>(job.Value);
                var jobOldValue = JsonConvert.DeserializeObject<List<DocItem>>(job.OldValue);
                if (jobValue == null)
                {
                    jobValue = new List<DocItem>();
                }
                if (jobOldValue == null)
                {
                    jobOldValue = new List<DocItem>();
                }

                var fullDocItemForDoc = lstDocItemFull.FirstOrDefault(x => x.DocInstanceId == job.DocInstanceId);
                if (fullDocItemForDoc != null && fullDocItemForDoc.DocItems != null && fullDocItemForDoc.DocItems.Count > 0)
                {
                    var docItemInJobOldValue = jobOldValue.Select(x => x.DocTypeFieldInstanceId).ToList();
                    var missDocItem = fullDocItemForDoc.DocItems.Where(x => !docItemInJobOldValue.Contains(x.DocTypeFieldInstanceId)).ToList();

                    if (missDocItem != null && missDocItem.Count > 0)
                    {
                        jobOldValue.AddRange(missDocItem); // bổ sung các meta bị thiếu
                        job.OldValue = JsonConvert.SerializeObject(jobOldValue); //update lại value của job
                    }
                }

                //Tại các bước cần hiện thị đủ meta -> thì luôn gán value=oldvalue để so khớp
                job.Value = job.OldValue;
            }

            return updatedJobs;
        }

        /// <summary>
        /// Bổ sung thông tin ShowInput cho từng item
        /// </summary>
        /// <param name="jobs"></param>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private async Task<List<Job>> AddDocTypeFieldExtraSetting(List<Job> jobs, string actionCode, string accessToken)
        {
            var updatedJobs = jobs;

            if (!jobs.Any())
            {
                return updatedJobs;
            }

            //duyệt từng job -> kiểm tra trong value nếu thiếu thì bổ sung
            Dictionary<Guid, List<DocTypeFieldDto>> dicDocTypeField = new Dictionary<Guid, List<DocTypeFieldDto>>();
            Dictionary<string, string> dicDocPath = new Dictionary<string, string>();
            foreach (var job in updatedJobs)
            {
                //get PathName
                string pathName = string.Empty;
                if (!string.IsNullOrEmpty(job.DocPath))
                {
                    if (!dicDocPath.ContainsKey(job.DocPath))
                    {
                        pathName = await GetPathName(job.DocPath, accessToken);
                        dicDocPath.Add(job.DocPath, pathName);
                    }
                    else
                    {
                        pathName = dicDocPath[job.DocPath];
                    }
                }

                job.PathName = pathName;

                List<DocTypeFieldDto> listDocTypeField = null;
                if (!dicDocTypeField.ContainsKey(job.DigitizedTemplateInstanceId.GetValueOrDefault()))
                {
                    var listdocTypeFieldRes = await _docTypeFieldClientService.GetByProjectAndDigitizedTemplateInstanceId(job.ProjectInstanceId.GetValueOrDefault(), job.DigitizedTemplateInstanceId.GetValueOrDefault(), accessToken);
                    listDocTypeField = listdocTypeFieldRes.Data;
                    dicDocTypeField.Add(job.DigitizedTemplateInstanceId.GetValueOrDefault(), listDocTypeField);
                }
                else
                {
                    listDocTypeField = dicDocTypeField[job.DigitizedTemplateInstanceId.GetValueOrDefault()];
                }

                //nếu job thuộc dạng xử lý đơn lẻ từng meta (ví dụ DataEntry)
                if (job.DocTypeFieldInstanceId != null)
                {
                    var docItem = listDocTypeField.FirstOrDefault(x => x.InstanceId == job.DocTypeFieldInstanceId);

                    job.DocTypeFieldSortOrder = docItem?.SortOrder ?? 0;
                    job.MinValue = docItem?.MinValue;
                    job.MaxValue = docItem?.MaxValue;
                    job.MinLength = docItem?.MinLength ?? 0;
                    job.MaxLength = docItem?.MaxLength ?? 0;
                    job.Format = docItem?.Format;
                    job.InputShortNote = docItem?.InputShortNote;
                    job.DocTypeFieldCode = docItem?.Code;
                    job.DocTypeFieldName = docItem?.Name;
                    job.InputType = docItem?.InputType ?? 0;
                    job.PrivateCategoryInstanceId = docItem?.PrivateCategoryInstanceId;
                    job.IsMultipleSelection = docItem?.IsMultipleSelection;
                }
                else // job xử lý nhiều meta ví dụ CheckFinal
                {
                    //lấy danh sách các DocTypeField theo DocInstanceId
                    if (string.IsNullOrEmpty(job.Value))
                    {
                        job.Value = JsonConvert.SerializeObject(new List<DocItem>());
                    }

                    if (string.IsNullOrEmpty(job.OldValue))
                    {
                        job.OldValue = JsonConvert.SerializeObject(new List<DocItem>());
                    }
                    var jobValue = JsonConvert.DeserializeObject<List<DocItem>>(job.Value);
                    var jobOldValue = JsonConvert.DeserializeObject<List<DocItem>>(job.OldValue);

                    if (jobValue != null)
                    {
                        //hotfix: loại bỏ duplicate
                        var groupedDocTypeFieldId = jobValue.GroupBy(x => x.DocTypeFieldInstanceId);
                        jobValue = groupedDocTypeFieldId.Select(g => g.First()).ToList();

                        foreach (var item in jobValue)
                        {
                            var docItem = listDocTypeField.FirstOrDefault(x => x.InstanceId == item.DocTypeFieldInstanceId);

                            item.ShowForInput = docItem?.ShowForInput ?? false;
                            item.DocTypeFieldSortOrder = docItem?.SortOrder ?? 0;
                            item.MinValue = docItem?.MinValue;
                            item.MaxValue = docItem?.MaxValue;
                            item.MinLength = docItem?.MinLength ?? 0;
                            item.MaxLength = docItem?.MaxLength ?? 0;
                            item.Format = docItem?.Format;
                            item.InputShortNote = docItem?.InputShortNote;
                            item.DocTypeFieldCode = docItem?.Code;
                            item.DocTypeFieldName = docItem?.Name;
                            item.InputType = docItem?.InputType ?? 0;
                            item.PrivateCategoryInstanceId = docItem?.PrivateCategoryInstanceId;
                            item.IsMultipleSelection = docItem?.IsMultipleSelection;
                        }
                        job.Value = JsonConvert.SerializeObject(jobValue); //update lại value của job
                    }

                    if (jobOldValue != null)
                    {
                        //hotfix: loại bỏ duplicate
                        var groupedDocTypeFieldId = jobOldValue.GroupBy(x => x.DocTypeFieldInstanceId);
                        jobOldValue = groupedDocTypeFieldId.Select(g => g.First()).ToList();

                        foreach (var item in jobOldValue)
                        {
                            var docItem = listDocTypeField.FirstOrDefault(x => x.InstanceId == item.DocTypeFieldInstanceId);

                            item.ShowForInput = docItem?.ShowForInput ?? false;
                            item.DocTypeFieldSortOrder = docItem?.SortOrder ?? 0;
                            item.MinValue = docItem?.MinValue;
                            item.MaxValue = docItem?.MaxValue;
                            item.MinLength = docItem?.MinLength ?? 0;
                            item.MaxLength = docItem?.MaxLength ?? 0;
                            item.Format = docItem?.Format;
                            item.InputShortNote = docItem?.InputShortNote;
                            item.DocTypeFieldCode = docItem?.Code;
                            item.DocTypeFieldName = docItem?.Name;
                            item.InputType = docItem?.InputType ?? 0;
                            item.PrivateCategoryInstanceId = docItem?.PrivateCategoryInstanceId;
                            item.IsMultipleSelection = docItem?.IsMultipleSelection;
                        }
                        job.OldValue = JsonConvert.SerializeObject(jobOldValue); //update lại value của job
                    }
                }

            }
            dicDocPath.Clear();
            dicDocTypeField.Clear();
            return updatedJobs;
        }

        /// <summary>
        /// Get Name of Path and cache it for 30 days
        /// </summary>
        /// <param name="docPath"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        private async Task<string> GetPathName(string docPath, string accessToken)
        {
            var pathName = string.Empty;
            var cacheTime = (int)TimeSpan.FromDays(30).TotalSeconds;
            var cacheKey = $"Job_PathName_{docPath}";
            pathName = _cachingHelper.TryGetFromCache<string>(cacheKey);
            if (string.IsNullOrEmpty(pathName))
            {
                var docRes = await _docClientService.GetPathName(docPath, accessToken);
                pathName = docRes.Data;
                await _cachingHelper.TrySetCacheAsync(cacheKey, pathName, cacheTime);
            }
            return pathName;
        }

        public async Task<GenericResponse<JobDto>> GetByInstanceId(Guid instanceId)
        {
            GenericResponse<JobDto> response;
            try
            {
                var filter = Builders<Job>.Filter.Eq(x => x.InstanceId, instanceId);
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<Job, JobDto>(data.FirstOrDefault());
                response = GenericResponse<JobDto>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<JobDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<JobDto>>> GetByInstanceIds(List<Guid> instanceIds)
        {
            GenericResponse<List<JobDto>> response;
            try
            {
                var filter = Builders<Job>.Filter.In(x => x.InstanceId, instanceIds);
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<List<Job>, List<JobDto>>(data);
                response = GenericResponse<List<JobDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<JobDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
