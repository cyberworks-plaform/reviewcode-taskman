using AutoMapper;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Implementations;
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
using Azure.Core;
using Ce.Auth.Client.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib;
using Ce.EventBus.Lib.Abstractions;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Ce.Workflow.Client.Services.Implementations;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Nest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static Nest.MachineLearningUsage;

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
        private readonly IUserConfigClientService _userConfigClientService;
        private readonly IMoneyService _moneyService;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IDocClientService _docClientService;
        private readonly IDocFieldValueClientService _docFieldValueClientService;

        private readonly ICachingHelper _cachingHelper;
        private readonly bool _useCache;
        private const string DataProcessing = "DataProcessing";
        private const int TimeOut = 600;   // Default HttpClient timeout is 100s
        public ComplainService(
            IComplainRepository repos,
            ISequenceComplainRepository sequenceComplainRepository,
            IUserConfigClientService userConfigClientService,
            IMoneyService moneyService,
            IWorkflowClientService workflowClientService,
            IDocClientService docClientService,
            IDocFieldValueClientService docFieldValueClientService,
            IJobRepository jobRepository,
            IMapper mapper,
            IUserPrincipalService userPrincipalService) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
            _sequenceComplainRepository = sequenceComplainRepository;
            _userConfigClientService = userConfigClientService;
            _moneyService = moneyService;
            _workflowClientService = workflowClientService;
            _docClientService = docClientService;
            _docFieldValueClientService = docFieldValueClientService;
            _jobRepository = jobRepository;
            _useCache = _cachingHelper != null;
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

        public async Task<GenericResponse<ComplainDto>> GetByInstanceId(string instanceId)
        {
            GenericResponse<ComplainDto> response;
            try
            {
                var complain = await _repository.GetByInstanceId(instanceId);
                var result = _mapper.Map<Complain, ComplainDto>(complain);
                response = GenericResponse<ComplainDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<ComplainDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<ComplainDto>> CreateOrUpdateComplain(ComplainDto complainDto, string accessToken = null)
        {
            GenericResponse<ComplainDto> response;
            try
            {
                if (string.IsNullOrEmpty(complainDto.Id))
                {
                    var complain = _mapper.Map<ComplainDto, Complain>(complainDto);
                    complain.Code = $"C{await _sequenceComplainRepository.GetSequenceValue("SequenceComplainName")}";
                    complain = await _repository.AddAsyncV2(complain);
                    complainDto.Id = complain.Id.ToString();
                    response = GenericResponse<ComplainDto>.ResultWithData(complainDto);
                }
                else
                {
                    var complain = _mapper.Map<ComplainDto, Complain>(complainDto);
                    if (complain.Status == (int)EnumComplainStatus.Complete)
                    {
                        var job = await _jobRepository.GetJobByInstanceId((Guid)complain.JobInstanceId);
                        if (job != null)
                        {
                            //TODO: Cập nhật Value bảng DocFieldValue
                            //var docFieldValue = await _docFieldValueClientService.GetByInstanceId(complain.DocFieldValueInstanceId.GetValueOrDefault(), accessToken);
                            //if (docFieldValue.Data != null)
                            //{
                            //    docFieldValue.Data.Value = complain.Value;
                            //    await _docFieldValueClientService.UpdateMulti(new List<DocFieldValueDto> { docFieldValue.Data });
                            //}

                            //TODO: Cập nhật FinalValue bảng Doc

                            //Tính lại rightStatus và Price các Job liên quan
                            var wfInfoes = await GetWfInfoes(job.WorkflowInstanceId.GetValueOrDefault(), accessToken);
                            var crrWfsJobsComplete = await _jobRepository.GetJobByWfs(
                                    job.DocInstanceId.GetValueOrDefault(), job.ActionCode,
                                    job.WorkflowStepInstanceId, (short)EnumJob.Status.Complete);
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
                                await _moneyService.ChargeMoneyForCompleteDocByField(wfInfoes.Item1, wfInfoes.Item2, docItems, complain.DocInstanceId.GetValueOrDefault(), complainDto.FieldChangeInstanceIds, accessToken);
                            }
                        }
                    }
                    complain = await _repository.UpdateAsync(complain);

                    response = GenericResponse<ComplainDto>.ResultWithData(complainDto);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<ComplainDto>.ResultWithError(-1, ex.Message, ex.StackTrace);
                Log.Error($"Error on CreateOrUpdateComplain => param: {JsonConvert.SerializeObject(complainDto)};mess: {ex.Message} ; trace:{ex.StackTrace}");
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
                        lastFilter = lastFilter & Builders<Complain>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(jobInstanceIdFilter.Value));
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
    public enum EnumComplainStatus
    {
        [Description("Loại bỏ")]
        Remove,
        [Description("Chưa xử lý")]
        Unprocessed,
        [Description("Đang xử lý")]
        Processing,
        [Description("Hoàn thành")]
        Complete
    }
}
