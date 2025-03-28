﻿using AutoMapper;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Model.Enums;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.Definitions;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    /// <summary>
    /// Initialize
    /// </summary>
    public partial class ComplainService : MongoBaseService<Complain, ComplainDto>, IComplainService
    {
        private readonly IComplainRepository _repository;
        private readonly IJobRepository _jobRepository;
        private readonly ISequenceComplainRepository _sequenceComplainRepository;
        private readonly IEventBus _eventBus;
        private readonly IUserConfigClientService _userConfigClientService;
        private readonly IMoneyService _moneyService;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IConfiguration _configuration;

        public ComplainService(
            IComplainRepository repos,
            ISequenceComplainRepository sequenceComplainRepository,
            IEventBus eventBus,
            IUserConfigClientService userConfigClientService,
            IMoneyService moneyService,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IJobRepository jobRepository,
            IMapper mapper,
            IUserPrincipalService userPrincipalService,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository,
            IConfiguration configuration) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
            _sequenceComplainRepository = sequenceComplainRepository;
            _eventBus = eventBus;
            _userConfigClientService = userConfigClientService;
            _moneyService = moneyService;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _jobRepository = jobRepository;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
            _configuration = configuration;
        }
    }


    public partial class ComplainService
    {
        public async Task<GenericResponse<ComplainDto>> GetByJobCode(string code)
        {
            GenericResponse<ComplainDto> response;
            try
            {
                var complain = await _repository.GetByJobCode(code);
                var result = _mapper.Map<Complain, ComplainDto>(complain);
                response = GenericResponse<ComplainDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<ComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        /// <summary>
        /// Todo: cần check lại logic update final value ở hàm Complain này => do đã bỏ bảng DocFieldValue
        /// </summary>
        /// <param name="model"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<GenericResponse<ComplainDto>> CreateOrUpdateComplain(ComplainDto model, string accessToken = null)
        {
            GenericResponse<ComplainDto> response;
            try
            {
                if (string.IsNullOrEmpty(model.Id)) // create new Complain
                {
                    var complain = _mapper.Map<ComplainDto, Complain>(model);
                    complain.Code = $"C{await _sequenceComplainRepository.GetSequenceValue("SequenceComplainName")}";
                    complain = await _repository.AddAsyncV2(complain);
                    model.Id = complain.Id.ToString();
                    response = GenericResponse<ComplainDto>.ResultWithData(model);
                }
                else // Update existed complain
                {
                    // Check has complain Processing
                    var hasComplainProcessing = await _repository.CheckComplainProcessing(model.DocInstanceId.GetValueOrDefault());
                    if (hasComplainProcessing)
                    {
                        response = GenericResponse<ComplainDto>.ResultWithData(model, "Phiếu có Khiếu nại đang được xử lý");
                    }
                    else
                    {
                        var complain = _mapper.Map<ComplainDto, Complain>(model);
                        complain = await _repository.UpdateAsync(complain);

                        if (complain.Status == (short)EnumComplain.Status.Processing)
                        {
                            var job = await _jobRepository.GetJobByInstanceId(complain.JobInstanceId.GetValueOrDefault());
                            if (job != null)
                            {
                                var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                                List<WorkflowStepInfo> wfsInfoes = wfInfoes?.Item1;
                                List<WorkflowSchemaConditionInfo> wfSchemaInfoes = wfInfoes?.Item2;
                                if (wfsInfoes != null && wfsInfoes.Any())
                                {
                                    var crrWfsInfo = wfsInfoes.First(x => x.InstanceId == job.WorkflowStepInstanceId);
                                    var docInstanceId = complain.DocInstanceId.GetValueOrDefault();

                                    var docItems = new List<DocItem>();
                                    var docItemComplains = new List<DocItemComplain>();

                                    var isUpdateValue = false;

                                    if (crrWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                    {
                                        docItemComplains = JsonConvert.DeserializeObject<List<DocItemComplain>>(complain.Value);

                                        if (docItemComplains != null && docItemComplains.Any())
                                        {
                                            var docTypeFieldInstanceIds = docItemComplains.Select(x => x.DocTypeFieldInstanceId.GetValueOrDefault()).Distinct().ToList();

                                            // 2. Update FinalValue in Doc: Chuẩn bị dữ liệu
                                            var docItemsRs = await _docClientService.GetDocItemByDocInstanceId(docInstanceId, accessToken);
                                            if (docItemsRs.Success == false)
                                            {
                                                throw new Exception($"Error calling DocClientService API: GetDocItemByDocInstanceId(). Error message: {docItemsRs.Message} ");
                                            }

                                            docItems = docItemsRs.Data;
                                            foreach (var docItem in docItems)
                                            {
                                                var crrDocItemComplain = docItemComplains.FirstOrDefault(x => x.DocTypeFieldInstanceId == docItem.DocTypeFieldInstanceId);
                                                if (crrDocItemComplain != null && crrDocItemComplain.Value != docItem.Value)
                                                {
                                                    docItem.Value = crrDocItemComplain.Value;
                                                    isUpdateValue = true;
                                                }
                                            }

                                        }
                                    }
                                    else
                                    {
                                        // Khiếu nại Đúng hoặc (khiếu nại Sai + chọn giá trị Khác) => Cập nhật giá trị & Giá tiền
                                        if (complain.RightStatus == (short)EnumComplain.RightStatus.Correct ||
                                            (complain.RightStatus == (short)EnumComplain.RightStatus.Wrong &&
                                             complain.ChooseValue == (short)EnumComplain.ChooseValue.Other &&
                                             complain.Value != complain.CompareValue))
                                        {
                                            isUpdateValue = true;
                                        }

                                        if (isUpdateValue)
                                        {

                                            // 2. Update FinalValue in Doc: Chuẩn bị dữ liệu
                                            var docItemsRs = await _docClientService.GetDocItemByDocInstanceId(docInstanceId, accessToken);
                                            if (docItemsRs.Success == false)
                                            {
                                                throw new Exception($"Error calling DocClientService API: GetDocItemByDocInstanceId(). Error message: {docItemsRs.Message} ");
                                            }

                                            docItems = docItemsRs.Data;
                                            foreach (var docItem in docItems)
                                            {
                                                if (docItem.DocTypeFieldInstanceId == complain.DocTypeFieldInstanceId)
                                                {
                                                    docItem.Value = complain.Value;
                                                }
                                            }

                                        }
                                    }
                                    // 2. Update FinalValue in Doc: Publish event
                                    if (isUpdateValue && docItems.Any())
                                    {
                                        var finalValue = JsonConvert.SerializeObject(docItems);
                                        var docUpdateFinalValueEvt = new DocUpdateFinalValueEvent
                                        {
                                            DocInstanceId = docInstanceId,
                                            FinalValue = finalValue
                                        };

                                        // Call Api
                                        var _ = await _docClientService.UpdateFinalValue(docUpdateFinalValueEvt, accessToken);
                                    }

                                    // 3. Cập nhật Giá tiền
                                    if (isUpdateValue)
                                    {
                                        await _moneyService.ChargeMoneyForComplainJob(wfsInfoes, wfSchemaInfoes, docItems, complain.DocInstanceId.GetValueOrDefault(), docItemComplains, accessToken);
                                    }
                                }
                            }

                            complain.Status = (short)EnumComplain.Status.Complete;
                            complain = await _repository.UpdateAsync(complain);
                        }

                        response = GenericResponse<ComplainDto>.ResultWithData(_mapper.Map<Complain, ComplainDto>(complain));
                    }
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<ComplainDto>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on CreateOrUpdateComplain => param: {JsonConvert.SerializeObject(model)};mess: {ex.Message} ; trace:{ex.StackTrace}");

                //rollback status
                try
                {
                    if (!string.IsNullOrEmpty(model.Id))
                    {
                        var complain = await _repository.GetByIdAsync(ObjectId.Parse(model.Id));
                        if (complain != null)
                        {
                            complain.Status = (short)EnumComplain.Status.Unprocessed;
                            complain.RightStatus = (short)EnumComplain.RightStatus.WaitingConfirm;

                            await _repository.UpdateAsync(complain);
                        }
                    }
                }
                catch (Exception exRollback)
                {
                    Log.Error($"Error on Rollback CreateOrUpdateComplain => param: {JsonConvert.SerializeObject(model)};mess: {exRollback.Message} ; trace:{exRollback.StackTrace}");
                }
            }
            
            return response;

        }
        public async Task<GenericResponse<HistoryComplainDto>> GetHistoryComplainByUser(PagingRequest request, string actionCode, string accessToken)
        {
            GenericResponse<HistoryComplainDto> response;
            try
            {
                if (_userPrincipalService == null)
                {
                    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }

                //var projectDefaultResponse = await _userConfigClientService.GetValueByCodeAsync(UserConfigCodeConstants.Project,
                //                accessToken);
                //if (!projectDefaultResponse.Success || projectDefaultResponse.Data == null || string.IsNullOrEmpty(projectDefaultResponse.Data))
                //{
                //    Log.Information("Không lấy được dự án");
                //    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
                //}
                //var projectDefault = JsonConvert.DeserializeObject<ProjectCache>(projectDefaultResponse.Data);
                //if (projectDefault == null)
                //{
                //    Log.Information("Không lấy được dự án");
                //    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
                //}
                var baseFilter = Builders<Complain>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                if (!string.IsNullOrEmpty(actionCode))
                {
                    baseFilter = baseFilter & Builders<Complain>.Filter.Eq(x => x.ActionCode, actionCode);
                }

                //var baseOrder = Builders<Complain>.Sort.Descending(nameof(Complain.LastModificationDate));
                var baseOrder = Builders<Complain>.Sort.Descending(nameof(Complain.CreatedDate));

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        //if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                        if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Gte(x => x.CreatedDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        //if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                        if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Lt(x => x.CreatedDate, endDate.ToUniversalTime());
                    }

                    //ComplainCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //JobInstanceId
                    var jobInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.JobInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (jobInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.JobInstanceId, Guid.Parse(jobInstanceIdFilter.Value));
                    }

                    //JobCode
                    var jobCodeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.JobCode)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (jobCodeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.JobCode, jobCodeFilter.Value);
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }
                    //else if (projectDefault != null)
                    //{
                    //    lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, projectDefault.InstanceId);
                    //}

                    //RightStatus =>//EnumComplain.RightStatus
                    var rightStatusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (rightStatusFilter != null)
                    {
                        var canParse = Int16.TryParse(rightStatusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("Status") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.Status, statusValue);
                        }

                    }
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Complain> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Complain).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Complain>.Sort.Ascending(item.Field)
                                   : Builders<Complain>.Sort.Descending(item.Field);
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
                var pagedList = new PagedListExtension<ComplainDto>
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
                    Data = _mapper.Map<List<ComplainDto>>(lst.Data)
                };

                var result = new HistoryComplainDto(pagedList);
                response = GenericResponse<HistoryComplainDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                response = GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<HistoryComplainDto>> GetPaging(PagingRequest request, string accessToken)
        {
            GenericResponse<HistoryComplainDto> response;
            try
            {
                if (_userPrincipalService == null)
                {
                    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, null, "Not Authorize");
                }

                var projectDefaultResponse = await _userConfigClientService.GetValueByCodeAsync(UserConfigCodeConstants.Project,
                                accessToken);
                if (!projectDefaultResponse.Success || projectDefaultResponse.Data == null || string.IsNullOrEmpty(projectDefaultResponse.Data))
                {
                    Log.Information("Không lấy được dự án");
                    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
                }
                var projectDefault = JsonConvert.DeserializeObject<ProjectCache>(projectDefaultResponse.Data);
                if (projectDefault == null)
                {
                    Log.Information("Không lấy được dự án");
                    return GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Chưa chọn dự án", "Chưa chọn dự án");
                }
                var baseFilter = Builders<Complain>.Filter.Gt(x => x.Status, 0) & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, projectDefault.InstanceId);

                //var baseOrder = Builders<Complain>.Sort.Descending(nameof(Complain.LastModificationDate));
                var baseOrder = Builders<Complain>.Sort.Descending(nameof(Complain.CreatedDate));

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    var actionCodeFilter = request.Filters.Where(_ => _.Field.Equals("ActionCode") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (actionCodeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(actionCodeFilter.Value.Trim()));
                    }
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        //if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                        if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Gte(x => x.CreatedDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        //if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                        if (canParse) lastFilter = lastFilter & Builders<Complain>.Filter.Lt(x => x.CreatedDate, endDate.ToUniversalTime());
                    }

                    //ComplainCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //JobInstanceId
                    var jobInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.JobInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (jobInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(jobInstanceIdFilter.Value));
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(ComplainDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }

                    //RightStatus =>//EnumComplain.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Complain> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Complain).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Complain>.Sort.Ascending(item.Field)
                                   : Builders<Complain>.Sort.Descending(item.Field);
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
                var pagedList = new PagedListExtension<ComplainDto>
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
                    Data = _mapper.Map<List<ComplainDto>>(lst.Data)
                };

                var result = new HistoryComplainDto(pagedList);
                response = GenericResponse<HistoryComplainDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {

                response = GenericResponse<HistoryComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<ComplainDto>> GetByInstanceId(Guid instanceId)
        {
            GenericResponse<ComplainDto> response;
            try
            {
                var filter = Builders<Complain>.Filter.Eq(x => x.InstanceId, instanceId);
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<Complain, ComplainDto>(data.FirstOrDefault());

                // Check has complain Processing
                dataDto.IsReadOnly = dataDto.DocInstanceId != null && await _repository.CheckComplainProcessing(dataDto.DocInstanceId.GetValueOrDefault());

                response = GenericResponse<ComplainDto>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<ComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<ComplainDto>>> GetByInstanceIds(List<Guid> instanceIds)
        {
            GenericResponse<List<ComplainDto>> response;
            try
            {
                var filter = Builders<Complain>.Filter.In(x => x.InstanceId, instanceIds);
                var data = await _repos.FindAsync(filter);
                var dataDto = _mapper.Map<List<Complain>, List<ComplainDto>>(data);
                response = GenericResponse<List<ComplainDto>>.ResultWithData(dataDto);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<ComplainDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        #region Private methods

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
